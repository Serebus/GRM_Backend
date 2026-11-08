namespace GRM_Backend.Models;

/// <summary>Valid status values for a Quotation.</summary>
public static class QuotationStatus
{
    public const string Draft    = "Draft";
    public const string Sent     = "Sent";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Converted = "Converted";
}

public class Quotation
{
    public int Id { get; set; }

    /// <summary>Customer who requested / owns this quote.</summary>
    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    /// <summary>Staff or Admin who created / manages this quote. Null until staff picks it up.</summary>
    public int? StaffId { get; set; }
    public User? Staff { get; set; }

    /// <summary>One of Draft | Sent | Accepted | Declined | Converted.</summary>
    public string Status { get; set; } = QuotationStatus.Draft;

    public string? Notes { get; set; }

    /// <summary>Discount percentage applied to the whole quotation (0–100).</summary>
    public decimal DiscountPercent { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    /// <summary>Customer-visible message when declining.</summary>
    public string? DeclineReason { get; set; }

    public List<QuotationItem> Items { get; set; } = new();

    // ── Computed helpers (not persisted) ────────────────────────────────
    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    public decimal DiscountAmount => Math.Round(Subtotal * DiscountPercent / 100, 2);
    public decimal Total => Subtotal - DiscountAmount;
}

public class QuotationItem
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public int Quantity { get; set; }

    /// <summary>Price snapshotted at quote creation time.</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>Lightweight sales order created when a quote is converted.</summary>
public class SalesOrder
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }
    public int CustomerId { get; set; }
    public User? Customer { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
