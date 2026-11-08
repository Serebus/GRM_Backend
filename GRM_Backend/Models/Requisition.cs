namespace GRM_Backend.Models;

public static class RequisitionStatus
{
    public const string Pending  = "Pending";
    public const string Approved = "Approved";
    public const string Declined = "Declined";
}

public class Requisition
{
    public int Id { get; set; }

    public int StaffId { get; set; }
    public User? Staff { get; set; }

    public string Status { get; set; } = RequisitionStatus.Pending;
    public string? Notes { get; set; }

    /// <summary>Admin who reviewed.</summary>
    public int? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }

    public string? ReviewNote { get; set; }

    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<RequisitionItem> Items { get; set; } = new();
}

public class RequisitionItem
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public Requisition? Requisition { get; set; }

    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public int RequestedQuantity { get; set; }
    public string? Notes { get; set; }
}
