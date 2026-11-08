using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;

    public OrdersController(AppDbContext context) => _context = context;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int    CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole   => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    private bool   IsStaffOrAdmin => CurrentRole is "Staff" or "Admin";
    private bool   IsAdmin        => CurrentRole == "Admin";

    /// <summary>Allowed status transitions for PATCH /status.</summary>
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new()
    {
        [OrderStatus.Pending]    = new() { OrderStatus.Processing, OrderStatus.Cancelled },
        [OrderStatus.Processing] = new() { OrderStatus.Completed,  OrderStatus.Cancelled },
        [OrderStatus.Completed]  = new(),   // terminal — only Admin correction can touch it
        [OrderStatus.Cancelled]  = new(),   // terminal
    };

    // ── Projection ────────────────────────────────────────────────────────────

    private static OrderDto ToDto(Order o) => new()
    {
        Id             = o.Id,
        CustomerId     = o.CustomerId,
        CustomerName   = o.Customer?.FullName ?? o.Customer?.Username ?? string.Empty,
        StaffId        = o.StaffId,
        StaffName      = o.Staff?.FullName ?? o.Staff?.Username,
        Status         = o.Status,
        Channel        = o.Channel,
        Notes          = o.Notes,
        CorrectionNote = o.CorrectionNote,
        Subtotal       = o.Subtotal,
        Total          = o.Total,
        CreatedAt      = o.CreatedAt,
        UpdatedAt      = o.UpdatedAt,
        CancelledAt    = o.CancelledAt,
        Items = o.Items.Select(i => new OrderItemDto
        {
            Id          = i.Id,
            VariantId   = i.VariantId,
            Sku         = i.Variant?.Sku         ?? string.Empty,
            ProductName = i.Variant?.Product?.Name ?? string.Empty,
            Model       = i.Variant?.Model        ?? string.Empty,
            Quantity    = i.Quantity,
            UnitPrice   = i.UnitPrice,
            LineTotal   = i.LineTotal
        }).ToList()
    };

    /// <summary>Loads an Order with all navigation properties.</summary>
    private async Task<Order?> FindOrder(int id) =>
        await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Staff)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                .ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

    /// <summary>
    /// Validates each requested item, checks stock, deducts inventory,
    /// and returns the built OrderItem list.  Returns null and writes
    /// a 400/404 response into <paramref name="result"/> on failure.
    /// </summary>
    private async Task<(List<OrderItem>? Items, IActionResult? Error)> BuildAndDeductItems(
        List<OrderItemRequest> requests)
    {
        var items = new List<OrderItem>();

        foreach (var r in requests)
        {
            var variant = await _context.Variants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == r.VariantId);

            if (variant == null || variant.Product is { IsActive: false })
                return (null, NotFound(new MessageResponse
                    { Message = $"Variant {r.VariantId} not found or inactive." }));

            if (variant.Stock < r.Quantity)
                return (null, BadRequest(new MessageResponse
                    { Message = $"Insufficient stock for SKU '{variant.Sku}' (requested {r.Quantity}, available {variant.Stock})." }));

            // Reserve stock immediately
            variant.Stock -= r.Quantity;
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid);
            _context.StockMovements.Add(new StockMovement
            {
                VariantId      = variant.Id,
                Type           = "StockOut",
                QuantityChange = -r.Quantity,
                StockAfter     = variant.Stock,
                Reason         = "Order placed",
                UserId         = uid == 0 ? null : uid
            });

            items.Add(new OrderItem
            {
                VariantId = variant.Id,
                Quantity  = r.Quantity,
                UnitPrice = variant.Price
            });
        }

        return (items, null);
    }

    /// <summary>Restores previously deducted stock when an order is cancelled.</summary>
    private async Task RestoreStock(Order order)
    {
        foreach (var item in order.Items)
        {
            var variant = await _context.Variants.FindAsync(item.VariantId);
            if (variant == null) continue;

            variant.Stock += item.Quantity;
            _context.StockMovements.Add(new StockMovement
            {
                VariantId      = variant.Id,
                Type           = "StockIn",
                QuantityChange = item.Quantity,
                StockAfter     = variant.Stock,
                Reason         = $"Order #{order.Id} cancelled",
                UserId         = CurrentUserId
            });
        }
    }

    // ── POST /api/orders  — Customer checkout ────────────────────────────────

    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        var uid = CurrentUserId;
        List<OrderItemRequest> lineItems;

        if (request.Items is { Count: > 0 })
        {
            lineItems = request.Items;
        }
        else
        {
            // Pull from the customer's cart
            var cartItems = await _context.CartItems
                .Include(c => c.Variant)
                .Where(c => c.UserId == uid)
                .ToListAsync();

            if (!cartItems.Any())
                return BadRequest(new MessageResponse { Message = "Your cart is empty. Add items before checking out." });

            lineItems = cartItems
                .Select(c => new OrderItemRequest { VariantId = c.VariantId, Quantity = c.Quantity })
                .ToList();
        }

        var (items, error) = await BuildAndDeductItems(lineItems);
        if (error != null) return error;

        var order = new Order
        {
            CustomerId = uid,
            Channel    = OrderChannel.Online,
            Notes      = request.Notes?.Trim(),
            Status     = OrderStatus.Pending,
            Items      = items!
        };

        _context.Orders.Add(order);

        // Clear the cart after a successful checkout
        var cart = _context.CartItems.Where(c => c.UserId == uid);
        _context.CartItems.RemoveRange(cart);

        await _context.SaveChangesAsync();

        var loaded = (await FindOrder(order.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(loaded));
    }

    // ── POST /api/orders/walk-in  — Staff / Admin in-store sale ─────────────

    [HttpPost("walk-in")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> WalkIn([FromBody] WalkInOrderRequest request)
    {
        var customer = await _context.Users.FindAsync(request.CustomerId);
        if (customer == null || customer.Role != "Customer")
            return BadRequest(new MessageResponse { Message = "CustomerId must reference a valid Customer account." });

        var (items, error) = await BuildAndDeductItems(request.Items);
        if (error != null) return error;

        var order = new Order
        {
            CustomerId = request.CustomerId,
            StaffId    = CurrentUserId,
            Channel    = OrderChannel.WalkIn,
            Notes      = request.Notes?.Trim(),
            Status     = OrderStatus.Pending,
            Items      = items!
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var loaded = (await FindOrder(order.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(loaded));
    }

    // ── GET /api/orders  — Staff / Admin: all orders with optional filters ───

    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetAll([FromQuery] OrderListQuery query)
    {
        var q = _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Staff)
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(o => o.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Channel))
            q = q.Where(o => o.Channel == query.Channel);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return Ok(orders.Select(ToDto));
    }

    // ── GET /api/orders/my  — Customer: own order history ───────────────────

    [HttpGet("my")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMy()
    {
        var uid    = CurrentUserId;
        var orders = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Staff)
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .Where(o => o.CustomerId == uid)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return Ok(orders.Select(ToDto));
    }

    // ── GET /api/orders/{id}  — Owner / Staff / Admin ────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await FindOrder(id);
        if (order == null) return NotFound(new MessageResponse { Message = "Order not found." });

        if (CurrentRole == "Customer" && order.CustomerId != CurrentUserId)
            return Forbid();

        return Ok(ToDto(order));
    }

    // ── PATCH /api/orders/{id}/status  — Staff / Admin ───────────────────────

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await FindOrder(id);
        if (order == null) return NotFound(new MessageResponse { Message = "Order not found." });

        var target = request.Status?.Trim() ?? string.Empty;

        // Validate the target status is a known value
        if (!AllowedTransitions.ContainsKey(target))
            return BadRequest(new MessageResponse
                { Message = $"Unknown status '{target}'. Valid values: {string.Join(", ", AllowedTransitions.Keys)}." });

        // Validate the transition is allowed
        if (!AllowedTransitions[order.Status].Contains(target))
            return BadRequest(new MessageResponse
                { Message = $"Cannot transition from '{order.Status}' to '{target}'." });

        // If transitioning to Cancelled, restore stock
        if (target == OrderStatus.Cancelled)
        {
            await RestoreStock(order);
            order.CancelledAt = DateTime.UtcNow;
        }

        order.Status    = target;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(ToDto(order));
    }

    // ── POST /api/orders/{id}/cancel  — Customer (Pending only) or Admin ─────

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await FindOrder(id);
        if (order == null) return NotFound(new MessageResponse { Message = "Order not found." });

        // Access control
        if (CurrentRole == "Customer")
        {
            if (order.CustomerId != CurrentUserId)
                return Forbid();

            if (order.Status != OrderStatus.Pending)
                return BadRequest(new MessageResponse
                    { Message = "Customers may only cancel Pending orders." });
        }
        else if (CurrentRole != "Admin")
        {
            // Staff may not cancel via this endpoint; use PATCH /status
            return Forbid();
        }

        if (order.Status == OrderStatus.Cancelled)
            return BadRequest(new MessageResponse { Message = "Order is already cancelled." });

        await RestoreStock(order);

        order.Status      = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.UpdatedAt   = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(ToDto(order));
    }

    // ── PUT /api/orders/{id}/correction  — Admin: correct a finalized order ──

    [HttpPut("{id:int}/correction")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Correction(int id, [FromBody] OrderCorrectionRequest request)
    {
        var order = await FindOrder(id);
        if (order == null) return NotFound(new MessageResponse { Message = "Order not found." });

        if (string.IsNullOrWhiteSpace(request.CorrectionNote))
            return BadRequest(new MessageResponse { Message = "A correctionNote is required." });

        // If new items are supplied, swap them out (restore old stock, deduct new)
        if (request.Items is { Count: > 0 })
        {
            // Restore stock for the existing items first
            await RestoreStock(order);

            // Deduct stock for the corrected items
            var (newItems, error) = await BuildAndDeductItems(request.Items);
            if (error != null) return error;

            _context.OrderItems.RemoveRange(order.Items);
            foreach (var item in newItems!) item.OrderId = order.Id;
            _context.OrderItems.AddRange(newItems);
            order.Items = newItems;
        }

        if (request.Notes != null)
            order.Notes = request.Notes.Trim();

        order.CorrectionNote = request.CorrectionNote.Trim();
        order.UpdatedAt      = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var loaded = (await FindOrder(id))!;
        return Ok(ToDto(loaded));
    }
}
