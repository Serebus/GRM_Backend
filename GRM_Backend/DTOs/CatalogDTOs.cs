using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

public class VariantRequest
{
    [Required, StringLength(100)] public string Model { get; set; } = string.Empty;
    [Required, StringLength(64)] public string Sku { get; set; } = string.Empty;
    [Range(0, 100000000)] public decimal Price { get; set; }
    [Range(0, int.MaxValue)] public int Stock { get; set; }
}

public class ProductRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    [Required, StringLength(100)] public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<VariantRequest>? Variants { get; set; }
}

public class ProductStatusRequest
{
    public bool IsActive { get; set; }
}

public class VariantDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal? MinPrice { get; set; }
    public int TotalStock { get; set; }
    public List<VariantDto> Variants { get; set; } = new();
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class BundleItemRequest
{
    public int VariantId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; } = 1;
}

public class BundleRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
    [Range(0, 100000000)] public decimal Price { get; set; }
    [Required, MinLength(1)] public List<BundleItemRequest> Items { get; set; } = new();
}

public class BundleItemDto
{
    public int VariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class BundleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public List<BundleItemDto> Items { get; set; } = new();
}
