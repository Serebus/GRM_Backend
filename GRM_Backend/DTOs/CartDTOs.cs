using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

public class AddCartItemRequest
{
    [Required] public int VariantId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
}

public class UpdateCartItemRequest
{
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
}

public class CartItemDto
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int Stock { get; set; }
    public decimal LineTotal { get; set; }
}

public class CartDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public int TotalQuantity { get; set; }
    public decimal Total { get; set; }
}
