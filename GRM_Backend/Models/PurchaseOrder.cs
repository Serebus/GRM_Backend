namespace GRM_Backend.Models;

public static class PurchaseOrderStatus
{
    public const string Draft             = "Draft";
    public const string Approved          = "Approved";
    public const string Sent              = "Sent";
    public const string PartiallyReceived = "PartiallyReceived";
    public const string Received          = "Received";
    public const string Cancelled         = "Cancelled";
}

public class PurchaseOrder
{
    public int Id { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Requisition this PO was raised from (optional).</summary>
    public int? RequisitionId { get; set; }
    public Requisition? Requisition { get; set; }

    public string Status { get; set; } = PurchaseOrderStatus.Draft;
    public string? Notes { get; set; }

    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public int? ApprovedById { get; set; }
    public User? ApprovedBy { get; set; }

    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SentAt     { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public List<PurchaseOrderItem> Items { get; set; } = new();

    public decimal TotalCost => Items.Sum(i => i.UnitCost * i.OrderedQuantity);
}

public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public int OrderedQuantity  { get; set; }
    public int ReceivedQuantity { get; set; } = 0;

    /// <summary>Cost price per unit from the supplier.</summary>
    public decimal UnitCost { get; set; }

    public decimal LineTotal => UnitCost * OrderedQuantity;
}
