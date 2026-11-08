using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>POST /api/orders/{id}/invoice  — generate invoice from an order.</summary>
public class GenerateInvoiceRequest
{
    public string? Notes { get; set; }
}

/// <summary>PATCH /api/invoices/{id}/payment-status</summary>
public class UpdatePaymentStatusRequest
{
    [Required]
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>Total amount paid so far (cumulative, not delta).</summary>
    [Range(0, double.MaxValue)]
    public decimal AmountPaid { get; set; }
}

/// <summary>POST /api/invoices/{id}/void</summary>
public class VoidInvoiceRequest
{
    [Required, MinLength(1)]
    public string Reason { get; set; } = string.Empty;
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class InvoiceLineDto
{
    public int     Id          { get; set; }
    public int     VariantId   { get; set; }
    public string  Sku         { get; set; } = string.Empty;
    public string  ProductName { get; set; } = string.Empty;
    public string  Model       { get; set; } = string.Empty;
    public int     Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
    public decimal LineTotal   { get; set; }
}

public class InvoiceDto
{
    public int     Id            { get; set; }
    public int     OrderId       { get; set; }
    public int     CustomerId    { get; set; }
    public string  CustomerName  { get; set; } = string.Empty;
    public int     IssuedById    { get; set; }
    public string  IssuedByName  { get; set; } = string.Empty;
    public decimal AmountDue     { get; set; }
    public decimal AmountPaid    { get; set; }
    public decimal Balance       { get; set; }
    public string  PaymentStatus { get; set; } = string.Empty;
    public bool    IsVoided      { get; set; }
    public string? VoidReason    { get; set; }
    public string? Notes         { get; set; }
    public DateTime  IssuedAt    { get; set; }
    public DateTime? UpdatedAt   { get; set; }
    public DateTime? VoidedAt    { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
}
