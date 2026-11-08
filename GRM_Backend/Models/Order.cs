namespace GRM_Backend.Models;

/// <summary>Valid status values for an Order.</summary>
public static class OrderStatus
{
    public const string Pending    = "Pending";
    public const string Processing = "Processing";
    public const string Completed  = "Completed";
    public const string Cancelled  = "Cancelled";
}

/// <summary>How the order was placed.</summary>
public static class OrderChannel
{
    public const string Online  = "Online";   // customer checkout
    public const string WalkIn  = "WalkIn";   // staff in-store sale
}

public class Order
{
    public int Id { get; set; }

    /// <summary>Customer who owns the order. For walk-ins this may be a staff-supplied customer id.</summary>
    public int CustomerId { get; set; }
    public User? Customer { get; set; }

    /// <summary>Staff or Admin who processed the order (always set for walk-in; set at checkout for online).</summary>
    public int? StaffId { get; set; }
    public User? Staff { get; set; }

    public string Status  { get; set; } = OrderStatus.Pending;
    public string Channel { get; set; } = OrderChannel.Online;

    public string? Notes { get; set; }

    /// <summary>Admin-only free-text explaining any post-completion correction.</summary>
    public string? CorrectionNote { get; set; }

    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }
    public DateTime? CancelledAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    // ── Computed helpers (not persisted) ──────────────────────────────
    public decimal Subtotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    public decimal Total    => Subtotal; // extend here if discounts / tax are added
}

public class OrderItem
{
    public int Id        { get; set; }
    public int OrderId   { get; set; }
    public Order? Order  { get; set; }

    public int VariantId          { get; set; }
    public ProductVariant? Variant { get; set; }

    public int Quantity   { get; set; }

    /// <summary>Price snapshotted at order creation time.</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}
