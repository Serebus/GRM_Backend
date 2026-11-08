using System.Security.Claims;
using System.Text;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly AppDbContext _context;
    public PurchaseOrdersController(AppDbContext context) => _context = context;

    private int    CurrentUserId  => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole    => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    private bool   IsAdmin        => CurrentRole == "Admin";

    // ── Allowed status transitions ────────────────────────────────────────────

    private static readonly Dictionary<string, string> ApproveFrom = new()
    {
        [PurchaseOrderStatus.Draft] = PurchaseOrderStatus.Approved
    };
    private static readonly Dictionary<string, string> SendFrom = new()
    {
        [PurchaseOrderStatus.Approved] = PurchaseOrderStatus.Sent
    };

    // ── Projection ────────────────────────────────────────────────────────────

    private static PurchaseOrderDto ToDto(PurchaseOrder po) => new()
    {
        Id             = po.Id,
        SupplierId     = po.SupplierId,
        SupplierName   = po.Supplier?.Name,
        RequisitionId  = po.RequisitionId,
        Status         = po.Status,
        Notes          = po.Notes,
        CreatedById    = po.CreatedById,
        CreatedByName  = po.CreatedBy?.FullName ?? po.CreatedBy?.Username ?? string.Empty,
        ApprovedById   = po.ApprovedById,
        ApprovedByName = po.ApprovedBy?.FullName ?? po.ApprovedBy?.Username,
        TotalCost      = po.TotalCost,
        CreatedAt      = po.CreatedAt,
        UpdatedAt      = po.UpdatedAt,
        ApprovedAt     = po.ApprovedAt,
        SentAt         = po.SentAt,
        ReceivedAt     = po.ReceivedAt,
        Items = po.Items.Select(i => new PurchaseOrderItemDto
        {
            Id               = i.Id,
            VariantId        = i.VariantId,
            Sku              = i.Variant?.Sku ?? string.Empty,
            ProductName      = i.Variant?.Product?.Name ?? string.Empty,
            Model            = i.Variant?.Model ?? string.Empty,
            OrderedQuantity  = i.OrderedQuantity,
            ReceivedQuantity = i.ReceivedQuantity,
            UnitCost         = i.UnitCost,
            LineTotal        = i.LineTotal
        }).ToList()
    };

    private async Task<PurchaseOrder?> FindPo(int id) =>
        await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Requisition)
            .Include(po => po.CreatedBy)
            .Include(po => po.ApprovedBy)
            .Include(po => po.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(po => po.Id == id);

    // ── Helper: build PO items from request ───────────────────────────────────

    private async Task<(List<PurchaseOrderItem>? Items, IActionResult? Error)> BuildItems(
        List<PoItemRequest> requests)
    {
        var items = new List<PurchaseOrderItem>();
        foreach (var r in requests)
        {
            var variant = await _context.Variants.Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == r.VariantId);
            if (variant == null || variant.Product is { IsActive: false })
                return (null, NotFound(new MessageResponse
                    { Message = $"Variant {r.VariantId} not found or inactive." }));

            items.Add(new PurchaseOrderItem
            {
                VariantId       = variant.Id,
                OrderedQuantity = r.OrderedQuantity,
                UnitCost        = r.UnitCost
            });
        }
        return (items, null);
    }

    // ── GET /api/purchase-orders?status=Draft ─────────────────────────────────

    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.CreatedBy)
            .Include(po => po.ApprovedBy)
            .Include(po => po.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(po => po.Status == status);

        var list = await query.OrderByDescending(po => po.CreatedAt).ToListAsync();
        return Ok(list.Select(ToDto));
    }

    // ── GET /api/purchase-orders/{id} ─────────────────────────────────────────

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetById(int id)
    {
        var po = await FindPo(id);
        return po == null
            ? NotFound(new MessageResponse { Message = "Purchase order not found." })
            : Ok(ToDto(po));
    }

    // ── POST /api/purchase-orders — Admin: create from requisition or draft ───

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseOrderRequest request)
    {
        List<PoItemRequest> lineItems = request.Items;

        // If a requisition is supplied, seed/merge items from it
        if (request.RequisitionId.HasValue)
        {
            var req = await _context.Requisitions
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == request.RequisitionId.Value);

            if (req == null)
                return NotFound(new MessageResponse { Message = "Requisition not found." });

            if (req.Status != RequisitionStatus.Approved)
                return BadRequest(new MessageResponse
                    { Message = "Only Approved requisitions can be converted to a purchase order." });

            // Merge: start from requisition items, overlay with any explicit overrides
            var overrideMap = request.Items.ToDictionary(i => i.VariantId);
            lineItems = req.Items.Select(ri => overrideMap.TryGetValue(ri.VariantId, out var ov)
                ? ov
                : new PoItemRequest { VariantId = ri.VariantId, OrderedQuantity = ri.RequestedQuantity })
                .ToList();
        }

        var (items, error) = await BuildItems(lineItems);
        if (error != null) return error;

        var po = new PurchaseOrder
        {
            SupplierId    = request.SupplierId,
            RequisitionId = request.RequisitionId,
            CreatedById   = CurrentUserId,
            Notes         = request.Notes?.Trim(),
            Status        = PurchaseOrderStatus.Draft,
            Items         = items!
        };

        _context.PurchaseOrders.Add(po);
        await _context.SaveChangesAsync();

        var loaded = (await FindPo(po.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = po.Id }, ToDto(loaded));
    }

    // ── PUT /api/purchase-orders/{id} — Admin: edit quantities or supplier ────

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseOrderRequest request)
    {
        var po = await FindPo(id);
        if (po == null) return NotFound(new MessageResponse { Message = "Purchase order not found." });

        if (po.Status != PurchaseOrderStatus.Draft)
            return BadRequest(new MessageResponse { Message = "Only Draft purchase orders can be edited." });

        if (request.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.FindAsync(request.SupplierId.Value);
            if (supplier == null || !supplier.IsActive)
                return BadRequest(new MessageResponse { Message = "Supplier not found or inactive." });
            po.SupplierId = request.SupplierId.Value;
        }

        if (request.Items is { Count: > 0 })
        {
            var (newItems, error) = await BuildItems(request.Items);
            if (error != null) return error;

            _context.PurchaseOrderItems.RemoveRange(po.Items);
            foreach (var item in newItems!) item.PurchaseOrderId = po.Id;
            _context.PurchaseOrderItems.AddRange(newItems);
            po.Items = newItems;
        }

        if (request.Notes != null) po.Notes = request.Notes.Trim();

        po.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var loaded = (await FindPo(id))!;
        return Ok(ToDto(loaded));
    }

    // ── POST /api/purchase-orders/{id}/approve — Admin ────────────────────────

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(int id)
    {
        var po = await FindPo(id);
        if (po == null) return NotFound(new MessageResponse { Message = "Purchase order not found." });

        if (po.Status != PurchaseOrderStatus.Draft)
            return BadRequest(new MessageResponse { Message = "Only Draft purchase orders can be approved." });

        if (!po.Items.Any())
            return BadRequest(new MessageResponse { Message = "Cannot approve an empty purchase order." });

        po.Status      = PurchaseOrderStatus.Approved;
        po.ApprovedById = CurrentUserId;
        po.ApprovedAt  = DateTime.UtcNow;
        po.UpdatedAt   = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(po));
    }

    // ── POST /api/purchase-orders/{id}/send — Admin ───────────────────────────

    [HttpPost("{id:int}/send")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Send(int id)
    {
        var po = await FindPo(id);
        if (po == null) return NotFound(new MessageResponse { Message = "Purchase order not found." });

        if (po.Status != PurchaseOrderStatus.Approved)
            return BadRequest(new MessageResponse { Message = "Only Approved purchase orders can be sent." });

        po.Status    = PurchaseOrderStatus.Sent;
        po.SentAt    = DateTime.UtcNow;
        po.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(po));
    }

    // ── POST /api/purchase-orders/{id}/receive — Staff / Admin ────────────────

    [HttpPost("{id:int}/receive")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Receive(int id, [FromBody] ReceivePurchaseOrderRequest request)
    {
        var po = await FindPo(id);
        if (po == null) return NotFound(new MessageResponse { Message = "Purchase order not found." });

        if (po.Status != PurchaseOrderStatus.Sent && po.Status != PurchaseOrderStatus.PartiallyReceived)
            return BadRequest(new MessageResponse
                { Message = "Only Sent or PartiallyReceived purchase orders can record deliveries." });

        foreach (var line in request.Lines)
        {
            var item = po.Items.FirstOrDefault(i => i.Id == line.PurchaseOrderItemId);
            if (item == null)
                return NotFound(new MessageResponse
                    { Message = $"PO line {line.PurchaseOrderItemId} not found on this order." });

            var newTotal = item.ReceivedQuantity + line.ReceivedQuantity;
            if (newTotal > item.OrderedQuantity)
                return BadRequest(new MessageResponse
                    { Message = $"Receiving {line.ReceivedQuantity} for line {item.Id} would exceed ordered quantity ({item.OrderedQuantity})." });

            var delta = line.ReceivedQuantity; // units newly received in this call

            // Update stock
            var variant = await _context.Variants.FindAsync(item.VariantId);
            if (variant != null)
            {
                variant.Stock += delta;
                _context.StockMovements.Add(new StockMovement
                {
                    VariantId      = variant.Id,
                    Type           = "StockIn",
                    QuantityChange = delta,
                    StockAfter     = variant.Stock,
                    Reason         = $"PO #{po.Id} received",
                    UserId         = CurrentUserId
                });
            }

            item.ReceivedQuantity = newTotal;
        }

        // Determine new PO status
        bool allReceived = po.Items.All(i => i.ReceivedQuantity >= i.OrderedQuantity);
        po.Status    = allReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        po.UpdatedAt = DateTime.UtcNow;
        if (allReceived) po.ReceivedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var loaded = (await FindPo(id))!;
        return Ok(ToDto(loaded));
    }

    // ── GET /api/purchase-orders/{id}/pdf — Admin ─────────────────────────────

    [HttpGet("{id:int}/pdf")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Pdf(int id)
    {
        var po = await FindPo(id);
        if (po == null) return NotFound(new MessageResponse { Message = "Purchase order not found." });

        return Content(BuildPdfHtml(ToDto(po)), "text/html", Encoding.UTF8);
    }

    // ── PDF builder ───────────────────────────────────────────────────────────

    private static string BuildPdfHtml(PurchaseOrderDto po)
    {
        var rows = new StringBuilder();
        foreach (var i in po.Items)
        {
            var received = $"{i.ReceivedQuantity}/{i.OrderedQuantity}";
            rows.Append(
                "<tr>" +
                $"<td>{i.ProductName} – {i.Model}</td>" +
                $"<td>{i.Sku}</td>" +
                $"<td style=\"text-align:right\">{i.OrderedQuantity}</td>" +
                $"<td style=\"text-align:right\">{received}</td>" +
                $"<td style=\"text-align:right\">{i.UnitCost:F2}</td>" +
                $"<td style=\"text-align:right\">{i.LineTotal:F2}</td>" +
                "</tr>");
        }

        var supplierRow  = po.SupplierName != null
            ? $"<p><span>Supplier:</span> {po.SupplierName}</p>" : "";
        var approvedRow  = po.ApprovedAt.HasValue
            ? $"<p><span>Approved:</span> {po.ApprovedAt:yyyy-MM-dd HH:mm} UTC by {po.ApprovedByName}</p>" : "";
        var sentRow      = po.SentAt.HasValue
            ? $"<p><span>Sent:</span> {po.SentAt:yyyy-MM-dd HH:mm} UTC</p>" : "";
        var receivedRow  = po.ReceivedAt.HasValue
            ? $"<p><span>Received:</span> {po.ReceivedAt:yyyy-MM-dd HH:mm} UTC</p>" : "";
        var reqRow       = po.RequisitionId.HasValue
            ? $"<p><span>Requisition #:</span> {po.RequisitionId}</p>" : "";
        var notesRow     = !string.IsNullOrWhiteSpace(po.Notes)
            ? $"<p><span>Notes:</span> {po.Notes}</p>" : "";

        return "<!DOCTYPE html>" +
               "<html lang=\"en\"><head><meta charset=\"utf-8\"/>" +
               $"<title>Purchase Order #{po.Id}</title>" +
               "<style>" +
               "body{font-family:Arial,sans-serif;margin:40px;color:#222;}" +
               "h1{color:#1a56a0;}" +
               "table{width:100%;border-collapse:collapse;margin-top:20px;}" +
               "th,td{border:1px solid #ccc;padding:8px 12px;}" +
               "th{background:#f0f4fa;}" +
               ".totals td{font-weight:bold;}" +
               ".meta{margin-bottom:20px;}" +
               ".meta span{display:inline-block;min-width:160px;color:#555;}" +
               $".{po.Status.Replace(" ", "")}{{display:inline-block;padding:3px 10px;border-radius:4px;font-size:.85em;font-weight:bold;}}" +
               "</style></head><body>" +
               $"<h1>Purchase Order #{po.Id}</h1>" +
               "<div class=\"meta\">" +
               supplierRow + reqRow +
               $"<p><span>Status:</span> {po.Status}</p>" +
               $"<p><span>Created:</span> {po.CreatedAt:yyyy-MM-dd HH:mm} UTC by {po.CreatedByName}</p>" +
               approvedRow + sentRow + receivedRow + notesRow +
               "</div>" +
               "<table><thead><tr>" +
               "<th>Product / Model</th><th>SKU</th><th>Ordered</th><th>Received</th><th>Unit Cost</th><th>Line Total</th>" +
               "</tr></thead><tbody>" +
               rows +
               "</tbody><tfoot class=\"totals\">" +
               $"<tr><td colspan=\"5\" style=\"text-align:right\"><strong>Total Cost</strong></td>" +
               $"<td style=\"text-align:right\"><strong>{po.TotalCost:F2}</strong></td></tr>" +
               "</tfoot></table></body></html>";
    }
}
