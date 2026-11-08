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
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly AppDbContext _context;

    public InvoicesController(AppDbContext context) => _context = context;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int    CurrentUserId  => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole    => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    private bool   IsStaffOrAdmin => CurrentRole is "Staff" or "Admin";
    private bool   IsAdmin        => CurrentRole == "Admin";

    private static readonly HashSet<string> ValidPaymentStatuses = new()
    {
        InvoicePaymentStatus.Unpaid,
        InvoicePaymentStatus.PartiallyPaid,
        InvoicePaymentStatus.Paid
    };

    // ── Projection ────────────────────────────────────────────────────────────

    private static InvoiceDto ToDto(Invoice inv) => new()
    {
        Id            = inv.Id,
        OrderId       = inv.OrderId,
        CustomerId    = inv.CustomerId,
        CustomerName  = inv.Customer?.FullName ?? inv.Customer?.Username ?? string.Empty,
        IssuedById    = inv.IssuedById,
        IssuedByName  = inv.IssuedBy?.FullName ?? inv.IssuedBy?.Username ?? string.Empty,
        AmountDue     = inv.AmountDue,
        AmountPaid    = inv.AmountPaid,
        Balance       = inv.Balance,
        PaymentStatus = inv.PaymentStatus,
        IsVoided      = inv.IsVoided,
        VoidReason    = inv.VoidReason,
        Notes         = inv.Notes,
        IssuedAt      = inv.IssuedAt,
        UpdatedAt     = inv.UpdatedAt,
        VoidedAt      = inv.VoidedAt,
        Lines = inv.Lines.Select(l => new InvoiceLineDto
        {
            Id          = l.Id,
            VariantId   = l.VariantId,
            Sku         = l.Sku,
            ProductName = l.ProductName,
            Model       = l.Model,
            Quantity    = l.Quantity,
            UnitPrice   = l.UnitPrice,
            LineTotal   = l.LineTotal
        }).ToList()
    };

    private async Task<Invoice?> FindInvoice(int id) =>
        await _context.Invoices
            .Include(inv => inv.Customer)
            .Include(inv => inv.IssuedBy)
            .Include(inv => inv.Lines)
            .FirstOrDefaultAsync(inv => inv.Id == id);

    // ── POST /api/orders/{id}/invoice  — Staff / Admin ───────────────────────

    [HttpPost("api/orders/{orderId:int}/invoice")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> Generate(int orderId, [FromBody] GenerateInvoiceRequest? request)
    {
        var order = await _context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return NotFound(new MessageResponse { Message = "Order not found." });

        if (order.Status == OrderStatus.Cancelled)
            return BadRequest(new MessageResponse { Message = "Cannot invoice a cancelled order." });

        // Prevent duplicate active invoices for the same order
        var existing = await _context.Invoices
            .FirstOrDefaultAsync(inv => inv.OrderId == orderId && !inv.IsVoided);
        if (existing != null)
            return Conflict(new MessageResponse
                { Message = $"An active invoice (#{existing.Id}) already exists for this order. Void it first to reissue." });

        // Snapshot the order lines into immutable InvoiceLines
        var lines = order.Items.Select(i => new InvoiceLine
        {
            VariantId   = i.VariantId,
            Sku         = i.Variant?.Sku          ?? string.Empty,
            ProductName = i.Variant?.Product?.Name ?? string.Empty,
            Model       = i.Variant?.Model         ?? string.Empty,
            Quantity    = i.Quantity,
            UnitPrice   = i.UnitPrice
        }).ToList();

        var invoice = new Invoice
        {
            OrderId       = order.Id,
            CustomerId    = order.CustomerId,
            IssuedById    = CurrentUserId,
            AmountDue     = order.Total,
            PaymentStatus = InvoicePaymentStatus.Unpaid,
            Notes         = request?.Notes?.Trim(),
            Lines         = lines
        };

        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        // Reload nav props for the response
        await _context.Entry(invoice).Reference(inv => inv.Customer).LoadAsync();
        await _context.Entry(invoice).Reference(inv => inv.IssuedBy).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, ToDto(invoice));
    }

    // ── GET /api/invoices  — Staff / Admin: all invoices ────────────────────

    [HttpGet("api/invoices")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetAll()
    {
        var invoices = await _context.Invoices
            .Include(inv => inv.Customer)
            .Include(inv => inv.IssuedBy)
            .Include(inv => inv.Lines)
            .OrderByDescending(inv => inv.IssuedAt)
            .ToListAsync();

        return Ok(invoices.Select(ToDto));
    }

    // ── GET /api/invoices/my  — Customer: own invoices ───────────────────────

    [HttpGet("api/invoices/my")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> GetMy()
    {
        var uid = CurrentUserId;
        var invoices = await _context.Invoices
            .Include(inv => inv.Customer)
            .Include(inv => inv.IssuedBy)
            .Include(inv => inv.Lines)
            .Where(inv => inv.CustomerId == uid)
            .OrderByDescending(inv => inv.IssuedAt)
            .ToListAsync();

        return Ok(invoices.Select(ToDto));
    }

    // ── GET /api/invoices/{id}  — Owner / Staff / Admin ─────────────────────

    [HttpGet("api/invoices/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var invoice = await FindInvoice(id);
        if (invoice == null) return NotFound(new MessageResponse { Message = "Invoice not found." });

        if (CurrentRole == "Customer" && invoice.CustomerId != CurrentUserId)
            return Forbid();

        return Ok(ToDto(invoice));
    }

    // ── GET /api/invoices/{id}/pdf  — Owner / Staff / Admin ─────────────────

    [HttpGet("api/invoices/{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var invoice = await FindInvoice(id);
        if (invoice == null) return NotFound(new MessageResponse { Message = "Invoice not found." });

        if (CurrentRole == "Customer" && invoice.CustomerId != CurrentUserId)
            return Forbid();

        return Content(BuildPdfHtml(invoice, ToDto(invoice)), "text/html", Encoding.UTF8);
    }

    // ── PATCH /api/invoices/{id}/payment-status  — Staff / Admin ────────────

    [HttpPatch("api/invoices/{id:int}/payment-status")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> UpdatePaymentStatus(int id, [FromBody] UpdatePaymentStatusRequest request)
    {
        var invoice = await FindInvoice(id);
        if (invoice == null) return NotFound(new MessageResponse { Message = "Invoice not found." });

        if (invoice.IsVoided)
            return BadRequest(new MessageResponse { Message = "Cannot update payment status of a voided invoice." });

        var targetStatus = request.PaymentStatus?.Trim() ?? string.Empty;
        if (!ValidPaymentStatuses.Contains(targetStatus))
            return BadRequest(new MessageResponse
                { Message = $"Invalid payment status '{targetStatus}'. Valid values: {string.Join(", ", ValidPaymentStatuses)}." });

        if (request.AmountPaid < 0 || request.AmountPaid > invoice.AmountDue)
            return BadRequest(new MessageResponse
                { Message = $"AmountPaid must be between 0 and {invoice.AmountDue:F2}." });

        // Auto-derive payment status from amount when it makes sense
        invoice.AmountPaid    = request.AmountPaid;
        invoice.PaymentStatus = targetStatus;
        invoice.UpdatedAt     = DateTime.UtcNow;

        // Guard: status must be consistent with amounts
        if (invoice.PaymentStatus == InvoicePaymentStatus.Paid && invoice.AmountPaid < invoice.AmountDue)
            return BadRequest(new MessageResponse
                { Message = "Cannot mark as Paid when AmountPaid is less than AmountDue." });

        if (invoice.PaymentStatus == InvoicePaymentStatus.Unpaid && invoice.AmountPaid > 0)
            return BadRequest(new MessageResponse
                { Message = "Cannot mark as Unpaid when AmountPaid is greater than zero." });

        await _context.SaveChangesAsync();
        return Ok(ToDto(invoice));
    }

    // ── POST /api/invoices/{id}/void  — Admin ────────────────────────────────

    [HttpPost("api/invoices/{id:int}/void")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Void(int id, [FromBody] VoidInvoiceRequest request)
    {
        var invoice = await FindInvoice(id);
        if (invoice == null) return NotFound(new MessageResponse { Message = "Invoice not found." });

        if (invoice.IsVoided)
            return BadRequest(new MessageResponse { Message = "Invoice is already voided." });

        invoice.IsVoided   = true;
        invoice.VoidReason = request.Reason.Trim();
        invoice.VoidedAt   = DateTime.UtcNow;
        invoice.UpdatedAt  = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(invoice));
    }

    // ── PDF builder ───────────────────────────────────────────────────────────

    private static string BuildPdfHtml(Invoice inv, InvoiceDto dto)
    {
        var voidBanner = inv.IsVoided
            ? "<div style=\"background:#fee2e2;border:2px solid #dc2626;padding:12px 20px;margin-bottom:20px;font-size:1.2em;font-weight:bold;color:#dc2626\">VOID" +
              (string.IsNullOrWhiteSpace(inv.VoidReason) ? "" : $" — {inv.VoidReason}") + "</div>"
            : "";

        var rows = new StringBuilder();
        foreach (var l in dto.Lines)
        {
            rows.Append(
                "<tr>" +
                $"<td>{l.ProductName} – {l.Model}</td>" +
                $"<td>{l.Sku}</td>" +
                $"<td style=\"text-align:right\">{l.Quantity}</td>" +
                $"<td style=\"text-align:right\">{l.UnitPrice:F2}</td>" +
                $"<td style=\"text-align:right\">{l.LineTotal:F2}</td>" +
                "</tr>");
        }

        var notesRow  = !string.IsNullOrWhiteSpace(dto.Notes)
            ? $"<p><span>Notes:</span> {dto.Notes}</p>" : "";
        var voidedRow = dto.IsVoided
            ? $"<p><span>Voided:</span> {dto.VoidedAt:yyyy-MM-dd HH:mm} UTC — {dto.VoidReason}</p>" : "";

        return "<!DOCTYPE html>" +
               "<html lang=\"en\"><head><meta charset=\"utf-8\"/>" +
               $"<title>Invoice #{dto.Id}</title>" +
               "<style>" +
               "body{font-family:Arial,sans-serif;margin:40px;color:#222;}" +
               "h1{color:#1a56a0;}" +
               "table{width:100%;border-collapse:collapse;margin-top:20px;}" +
               "th,td{border:1px solid #ccc;padding:8px 12px;}" +
               "th{background:#f0f4fa;}" +
               ".totals td{font-weight:bold;}" +
               ".meta{margin-bottom:20px;}" +
               ".meta span{display:inline-block;min-width:160px;color:#555;}" +
               ".badge{display:inline-block;padding:3px 10px;border-radius:4px;font-size:.85em;font-weight:bold;}" +
               ".Unpaid{background:#fee2e2;color:#dc2626;}" +
               ".PartiallyPaid{background:#fef3c7;color:#d97706;}" +
               ".Paid{background:#d1fae5;color:#065f46;}" +
               "</style></head><body>" +
               voidBanner +
               $"<h1>Invoice #{dto.Id}</h1>" +
               "<div class=\"meta\">" +
               $"<p><span>Order #:</span> {dto.OrderId}</p>" +
               $"<p><span>Customer:</span> {dto.CustomerName}</p>" +
               $"<p><span>Issued by:</span> {dto.IssuedByName}</p>" +
               $"<p><span>Issued:</span> {dto.IssuedAt:yyyy-MM-dd HH:mm} UTC</p>" +
               $"<p><span>Payment status:</span> <span class=\"badge {dto.PaymentStatus}\">{dto.PaymentStatus}</span></p>" +
               notesRow + voidedRow +
               "</div>" +
               "<table><thead><tr>" +
               "<th>Product / Model</th><th>SKU</th><th>Qty</th><th>Unit Price</th><th>Line Total</th>" +
               "</tr></thead><tbody>" +
               rows +
               "</tbody><tfoot class=\"totals\">" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\">Amount Due</td><td style=\"text-align:right\">{dto.AmountDue:F2}</td></tr>" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\">Amount Paid</td><td style=\"text-align:right\">{dto.AmountPaid:F2}</td></tr>" +
               $"<tr><td colspan=\"4\" style=\"text-align:right\"><strong>Balance</strong></td><td style=\"text-align:right\"><strong>{dto.Balance:F2}</strong></td></tr>" +
               "</tfoot></table></body></html>";
    }
}
