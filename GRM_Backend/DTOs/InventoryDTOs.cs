using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

public class StockInRequest
{
    [Required] public int VariantId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    public string? Reason { get; set; }
}

public class StockOutRequest
{
    [Required] public int VariantId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
    [Required, MinLength(1)] public string Reason { get; set; } = string.Empty;
}

public class StockAdjustmentRequest
{
    [Required] public int VariantId { get; set; }
    [Range(0, int.MaxValue)] public int NewStock { get; set; }
    [Required, MinLength(1)] public string Reason { get; set; } = string.Empty;
}

public class InventoryItemDto
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Stock { get; set; }
    public int ReorderThreshold { get; set; }
}

public class StockMovementDto
{
    public int Id { get; set; }
    public int VariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public int StockAfter { get; set; }
    public string? Reason { get; set; }
    public int? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
