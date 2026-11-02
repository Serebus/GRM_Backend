namespace GRM_Backend.Models;

public class CartItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    public int Quantity { get; set; }
}
