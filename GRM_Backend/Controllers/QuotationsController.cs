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
[Route("api/quotations")]
[Authorize]
public class QuotationsController : ControllerBase
{
    // Maximum discount Staff members are allowed to grant.
    private const decimal StaffMaxDiscount = 15m;

    private readonly AppDbContext _context;

    public QuotationsController(AppDbContext context)
    {
        _context = context;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    private bool IsStaffOrAdmin => CurrentRole is "Staff" or "Admin";

    /// <summary>Projects a Quotation entity to its DTO (items must be loaded).</summary>
    private static QuotationDto ToDto(Quotation q) => new()
    {
        Id             = q.Id,
        CustomerId     = q.CustomerId,
        CustomerName   = q.Customer?.FullName ?? q.Customer?.Username ?? string.Empty,
        StaffId        = q.StaffId,
        StaffName      = q.Staff?.FullName ?? q.Staff?.Username,
        Status         = q.Status,
        Notes          = q.Notes,
        DiscountPercent  = q.DiscountPercent,
        Subtotal       = q.Subtotal,
        DiscountAmount = q.DiscountAmount,
        Total          = q.Total,
        CreatedAt      = q.CreatedAt,
        UpdatedAt      = q.UpdatedAt,
        SentAt         = q.SentAt,
        RespondedAt    = q.RespondedAt,
        DeclineReason  = q.DeclineReason,
        Items = q.Items.Select(i => new QuotationItemDto
        {
            Id          = i.Id,
            VariantId   = i.VariantId,
            Sku         = i.Variant?.Sku ?? string.Empty,
            ProductName = i.Variant?.Product?.Name ?? string.Empty,
            Model       = i.Variant?.Model ?? string.Empty,
            Quantity    = i.Quantity,
            UnitPrice   = i.UnitPrice,
            LineTotal   = i.LineTotal
        }).ToList()
    };

    /// <summary>Loads a quotation with all navigation properties.</summary>
    private async Task<Quotation?> FindQuotation(int id) =>
        await _context.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Staff)
            .Include(q => q.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(q => q.Id == id);

    /// <summary>Validates and builds QuotationItem list from a request item list.
    /// Returns null and writes a 400 response if validation fails.</summary>
    private async Task<List<QuotationItem>?> BuildItems(List<QuoteItemRequest> requests)
    {
        var items = new List<QuotationItem>();
        foreach (var r in requests)
        {
            var variant = await _context.Variants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == r.VariantId);

            if (variant == null || variant.Product is { IsActive: false })
            {
                ModelState.AddModelError("items", $"Variant {r.VariantId} not found or inactive.");
                return null;
            }

            items.Add(new QuotationItem
            {
                VariantId = variant.Id,
                Quantity  = r.Quantity,
                UnitPrice = variant.Price
            });
        }
        return items;
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    // POST /api/quotations/requests  — Customer submits a quote request
    [HttpPost("requests")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> RequestQuote([FromBody] QuoteRequestBody body)
    {
        var items = await BuildItems(body.Items);
        if (items == null) return ValidationProblem(ModelState);

        var quotation = new Quotation
        {
            CustomerId = CurrentUserId,
            Notes      = body.Notes?.Trim(),
            Status     = QuotationStatus.Draft,
            Items      = items
        };

        _context.Quotations.Add(quotation);
        await _context.SaveChangesAsync();

        var loaded = (await FindQuotation(quotation.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = quotation.Id }, ToDto(loaded));
    }

    // POST /api/quotations  — Staff / Admin creates a quote directly
    [HttpPost]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateQuotationRequest request)
    {
        // Enforce role-based discount ceiling
        if (CurrentRole == "Staff" && request.DiscountPercent > StaffMaxDiscount)
            return BadRequest(new MessageResponse
            {
                Message = $"Staff members may not apply a discount greater than {StaffMaxDiscount}%."
            });

        var customer = await _context.Users.FindAsync(request.CustomerId);
        if (customer == null || customer.Role != "Customer")
            return BadRequest(new MessageResponse { Message = "CustomerId must reference a valid Customer account." });

        var items = await BuildItems(request.Items);
        if (items == null) return ValidationProblem(ModelState);

        var quotation = new Quotation
        {
            CustomerId      = request.CustomerId,
            StaffId         = CurrentUserId,
            DiscountPercent = request.DiscountPercent,
            Notes           = request.Notes?.Trim(),
            Status          = QuotationStatus.Draft,
            Items           = items
        };

        _context.Quotations.Add(quotation);
        await _context.SaveChangesAsync();

        var loaded = (await FindQuotation(quotation.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = quotation.Id }, ToDto(loaded));
    }

    // GET /api/quotations  — Staff / Admin: all quotes
    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetAll()
    {
        var quotes = await _context.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Staff)
            .Include(q => q.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();

        return Ok(quotes.Select(ToDto));
    }

    // GET /api/quotations/my  — Customer: own quotes only
    [HttpGet("my")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMy()
    {
        var uid = CurrentUserId;
        var quotes = await _context.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Staff)
            .Include(q => q.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .Where(q => q.CustomerId == uid)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();

        return Ok(quotes.Select(ToDto));
    }

    // GET /api/quotations/{id}  — Staff/Admin or the owning Customer
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        // Customers can only see their own quotations
        if (CurrentRole == "Customer" && quotation.CustomerId != CurrentUserId)
            return Forbid();

        return Ok(ToDto(quotation));
    }

    // PUT /api/quotations/{id}  — Staff / Admin: edit a draft
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateQuotationRequest request)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        if (quotation.Status != QuotationStatus.Draft)
            return BadRequest(new MessageResponse { Message = "Only Draft quotations can be edited." });

        // Discount ceiling
        if (request.DiscountPercent.HasValue)
        {
            if (CurrentRole == "Staff" && request.DiscountPercent.Value > StaffMaxDiscount)
                return BadRequest(new MessageResponse
                {
                    Message = $"Staff members may not apply a discount greater than {StaffMaxDiscount}%."
                });
            quotation.DiscountPercent = request.DiscountPercent.Value;
        }

        if (request.Notes != null)
            quotation.Notes = request.Notes.Trim();

        if (request.Items != null)
        {
            // Replace existing items
            _context.QuotationItems.RemoveRange(quotation.Items);
            var newItems = await BuildItems(request.Items);
            if (newItems == null) return ValidationProblem(ModelState);
            foreach (var item in newItems) item.QuotationId = quotation.Id;
            _context.QuotationItems.AddRange(newItems);
            quotation.Items = newItems;
        }

        quotation.UpdatedAt = DateTime.UtcNow;
        quotation.StaffId   = CurrentUserId;
        await _context.SaveChangesAsync();

        var loaded = (await FindQuotation(id))!;
        return Ok(ToDto(loaded));
    }

    // POST /api/quotations/{id}/send  — Staff / Admin: mark as Sent
    [HttpPost("{id:int}/send")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Send(int id)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        if (quotation.Status != QuotationStatus.Draft)
            return BadRequest(new MessageResponse { Message = "Only Draft quotations can be sent." });

        if (!quotation.Items.Any())
            return BadRequest(new MessageResponse { Message = "Cannot send an empty quotation." });

        quotation.Status    = QuotationStatus.Sent;
        quotation.SentAt    = DateTime.UtcNow;
        quotation.UpdatedAt = DateTime.UtcNow;
        quotation.StaffId ??= CurrentUserId;

        await _context.SaveChangesAsync();
        return Ok(ToDto(quotation));
    }

    // POST /api/quotations/{id}/accept  — Customer responds
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Accept(int id, [FromBody] RespondQuotationRequest? body)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        if (quotation.CustomerId != CurrentUserId)
            return Forbid();

        if (quotation.Status != QuotationStatus.Sent)
            return BadRequest(new MessageResponse { Message = "Only Sent quotations can be accepted." });

        quotation.Status      = QuotationStatus.Accepted;
        quotation.RespondedAt = DateTime.UtcNow;
        quotation.UpdatedAt   = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(quotation));
    }

    // POST /api/quotations/{id}/decline  — Customer declines
    [HttpPost("{id:int}/decline")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Decline(int id, [FromBody] RespondQuotationRequest? body)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        if (quotation.CustomerId != CurrentUserId)
            return Forbid();

        if (quotation.Status != QuotationStatus.Sent)
            return BadRequest(new MessageResponse { Message = "Only Sent quotations can be declined." });

        quotation.Status        = QuotationStatus.Declined;
        quotation.DeclineReason = body?.Reason?.Trim();
        quotation.RespondedAt   = DateTime.UtcNow;
        quotation.UpdatedAt     = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(quotation));
    }

    // POST /api/quotations/{id}/convert  — Staff / Admin: convert Accepted quote to SalesOrder
    [HttpPost("{id:int}/convert")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Convert(int id)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        if (quotation.Status != QuotationStatus.Accepted)
            return BadRequest(new MessageResponse { Message = "Only Accepted quotations can be converted." });

        // Guard: prevent double conversion
        var alreadyConverted = await _context.SalesOrders.AnyAsync(o => o.QuotationId == id);
        if (alreadyConverted)
            return Conflict(new MessageResponse { Message = "This quotation has already been converted to a sales order." });

        var order = new SalesOrder
        {
            QuotationId = quotation.Id,
            CustomerId  = quotation.CustomerId,
            Total       = quotation.Total
        };

        quotation.Status    = QuotationStatus.Converted;
        quotation.UpdatedAt = DateTime.UtcNow;

        _context.SalesOrders.Add(order);
        await _context.SaveChangesAsync();

        // Reload customer name for the response
        await _context.Entry(order).Reference(o => o.Customer).LoadAsync();

        return Ok(new SalesOrderDto
        {
            Id           = order.Id,
            QuotationId  = order.QuotationId,
            CustomerId   = order.CustomerId,
            CustomerName = order.Customer?.FullName ?? order.Customer?.Username ?? string.Empty,
            Total        = order.Total,
            CreatedAt    = order.CreatedAt
        });
    }

    // GET /api/quotations/{id}/pdf  — Owner / Staff / Admin: printable version
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var quotation = await FindQuotation(id);
        if (quotation == null) return NotFound(new MessageResponse { Message = "Quotation not found." });

        // Customers can only retrieve their own quote's PDF
        if (CurrentRole == "Customer" && quotation.CustomerId != CurrentUserId)
            return Forbid();

        var dto = ToDto(quotation);
        var html = BuildPdfHtml(dto);

        // Return the HTML as an inline document; in production a headless renderer
        // (e.g. PuppeteerSharp, WkHtmlToPdf) would convert this to actual PDF bytes.
        return Content(html, "text/html", Encoding.UTF8);
    }

    // ── PDF builder (simple HTML template) ───────────────────────────────────

    private static string BuildPdfHtml(QuotationDto q)
    {
        var rows = new StringBuilder();
        foreach (var item in q.Items)
        {
            rows.Append(
                $"<tr>" +
                $"<td>{item.ProductName} – {item.Model}</td>" +
                $"<td>{item.Sku}</td>" +
                $"<td style=\"text-align:right\">{item.Quantity}</td>" +
                $"<td style=\"text-align:right\">{item.UnitPrice:F2}</td>" +
                $"<td style=\"text-align:right\">{item.LineTotal:F2}</td>" +
                $"</tr>");
        }

        var sentRow  = q.SentAt.HasValue ? $"<p><span>Sent:</span> {q.SentAt:yyyy-MM-dd HH:mm} UTC</p>" : "";
        var notesRow = !string.IsNullOrWhiteSpace(q.Notes) ? $"<p><span>Notes:</span> {q.Notes}</p>" : "";
        var staffName = q.StaffName ?? "—";

        return "<!DOCTYPE html>" +
               "<html lang=\"en\"><head><meta charset=\"utf-8\"/>" +
               $"<title>Quotation #{q.Id}</title>" +
               "<style>" +
               "body{font-family:Arial,sans-serif;margin:40px;color:#222;}" +
               "h1{color:#1a56a0;}" +
               "table{width:100%;border-collapse:collapse;margin-top:20px;}" +
               "th,td{border:1px solid #ccc;padding:8px 12px;}" +
               "th{background:#f0f4fa;}" +
               ".totals td{font-weight:bold;}" +
               ".meta{margin-bottom:20px;}" +
               ".meta span{display:inline-block;min-width:140px;color:#555;}" +
               "</style></head><body>" +
               $"<h1>Quotation #{q.Id}</h1>" +
               "<div class=\"meta\">" +
               $"<p><span>Customer:</span> {q.CustomerName}</p>" +
               $"<p><span>Handled by:</span> {staffName}</p>" +
               $"<p><span>Status:</span> {q.Status}</p>" +
               $"<p><span>Created:</span> {q.CreatedAt:yyyy-MM-dd HH:mm} UTC</p>" +
               sentRow + notesRow +
               "</div>" +
               "<table><thead><tr>" +
               "<th>Product / Model</th><th>SKU</th><th>Qty</th><th>Unit Price</th><th>Line Total</th>" +
               "</tr></thead><tbody>" +
               rows +
               "</tbody><tfoot class=\"totals\">" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\">Subtotal</td><td style=\"text-align:right\">{q.Subtotal:F2}</td></tr>" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\">Discount ({q.DiscountPercent}%)</td><td style=\"text-align:right\">-{q.DiscountAmount:F2}</td></tr>" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\"><strong>Total</strong></td><td style=\"text-align:right\"><strong>{q.Total:F2}</strong></td></tr>" +
               "</tfoot></table></body></html>";
    }
}
