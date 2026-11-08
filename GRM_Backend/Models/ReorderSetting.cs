namespace GRM_Backend.Models;

/// <summary>Admin-configurable per-variant reorder settings.
/// Extends / overrides the base ReorderThreshold on ProductVariant.</summary>
public class ReorderSetting
{
    public int Id { get; set; }

    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    /// <summary>Overrides ProductVariant.ReorderThreshold when set.</summary>
    public int? ThresholdOverride { get; set; }

    /// <summary>Default quantity to include in a PO when this variant is suggested.</summary>
    public int DefaultOrderQuantity { get; set; } = 10;

    /// <summary>Lookback window (days) used for sales-velocity calculation.</summary>
    public int VelocityWindowDays { get; set; } = 30;

    public int? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
