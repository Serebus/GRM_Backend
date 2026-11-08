namespace GRM_Backend.Models;

/// <summary>Valid payment-status values for an Invoice.</summary>
public static class InvoicePaymentStatus
{
    public const string Unpaid        = "Unpaid";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid          = "Paid";
}

public class Invoice
{
    public int Id { get; set; }

    /// <summary>The order this invoice was generated from.</summary>
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>Convenience FK — duplicated from Order for query simplicity.</summary>
    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    /// <summary>Staff or Admin who generated the invoice.</summary>
    public int IssuedById { get; set; }
    public User? IssuedBy { get; set; }

    /// <summary>Snapshot of the order total at generation time.</summary>
    public decimal AmountDue { get; set; }

    /// <summary>Amount received so far.</summary>
    public decimal AmountPaid { get; set; } = 0;

    public decimal Balance => AmountDue - AmountPaid;

    /// <summary>Unpaid | PartiallyPaid | Paid</summary>
    public string PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;

    public bool IsVoided { get; set; } = false;

    /// <summary>Admin-supplied reason when voiding.</summary>
    public string? VoidReason { get; set; }

    /// <summary>Snapshot of line items so the invoice is immutable even if the order is corrected.</summary>
    public List<InvoiceLine> Lines { get; set; } = new();

    public DateTime IssuedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? VoidedAt  { get; set; }

    /// <summary>Optional free-text note added at generation time.</summary>
    public string? Notes { get; set; }
}

/// <summary>Immutable snapshot of one order line, stored on the invoice.</summary>
public class InvoiceLine
{
    public int Id        { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public int    VariantId   { get; set; }
    public string Sku         { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Model       { get; set; } = string.Empty;
    public int    Quantity    { get; set; }
    public decimal UnitPrice  { get; set; }
    public decimal LineTotal  => UnitPrice * Quantity;
}
