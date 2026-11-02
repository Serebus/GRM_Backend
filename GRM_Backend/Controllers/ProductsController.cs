using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProductsController(AppDbContext context) => _context = context;

    internal static ProductDto ToDto(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Category = p.Category,
        IsActive = p.IsActive,
        MinPrice = p.Variants.Count > 0 ? p.Variants.Min(v => v.Price) : null,
        TotalStock = p.Variants.Sum(v => v.Stock),
        Variants = p.Variants.OrderBy(v => v.Id).Select(ToDto).ToList()
    };

    internal static VariantDto ToDto(ProductVariant v) => new()
    {
        Id = v.Id, ProductId = v.ProductId, Model = v.Model, Sku = v.Sku, Price = v.Price, Stock = v.Stock
    };

    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet("api/products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? category,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Products.Include(p => p.Variants).AsQueryable();
        if (!(User.Identity?.IsAuthenticated == true && IsAdmin))
            query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s)
                                     || (p.Description != null && p.Description.ToLower().Contains(s))
                                     || p.Variants.Any(v => v.Sku.ToLower().Contains(s) || v.Model.ToLower().Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim().ToLower();
            query = query.Where(p => p.Category.ToLower() == c);
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<ProductDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("api/products/{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id)
    {
        var p = await _context.Products.Include(x => x.Variants).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null || (!p.IsActive && !(User.Identity?.IsAuthenticated == true && IsAdmin)))
            return NotFound(new MessageResponse { Message = "Product not found." });
        return Ok(ToDto(p));
    }

    [HttpPost("api/products")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] ProductRequest request)
    {
        var skus = request.Variants?.Select(v => v.Sku.Trim()).ToList() ?? new();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count)
            return Conflict(new MessageResponse { Message = "Duplicate SKU in request." });
        foreach (var sku in skus)
            if (await SkuExists(sku, null))
                return Conflict(new MessageResponse { Message = $"SKU '{sku}' already exists." });

        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Category = request.Category.Trim(),
            IsActive = request.IsActive,
            Variants = request.Variants?.Select(v => new ProductVariant
            {
                Model = v.Model.Trim(), Sku = v.Sku.Trim(), Price = v.Price, Stock = v.Stock
            }).ToList() ?? new()
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ToDto(product));
    }

    [HttpPut("api/products/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] ProductRequest request)
    {
        var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(new MessageResponse { Message = "Product not found." });

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Category = request.Category.Trim();
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(ToDto(product));
    }

    [HttpPatch("api/products/{id:int}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStatus(int id, [FromBody] ProductStatusRequest request)
    {
        var product = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(new MessageResponse { Message = "Product not found." });
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(ToDto(product));
    }

    [HttpPost("api/products/{id:int}/variants")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(VariantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddVariant(int id, [FromBody] VariantRequest request)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound(new MessageResponse { Message = "Product not found." });
        if (await SkuExists(request.Sku.Trim(), null))
            return Conflict(new MessageResponse { Message = "SKU already exists." });

        var variant = new ProductVariant
        {
            ProductId = id, Model = request.Model.Trim(), Sku = request.Sku.Trim(),
            Price = request.Price, Stock = request.Stock
        };
        _context.Variants.Add(variant);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id }, ToDto(variant));
    }

    [HttpPut("api/variants/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(VariantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateVariant(int id, [FromBody] VariantRequest request)
    {
        var variant = await _context.Variants.FindAsync(id);
        if (variant == null) return NotFound(new MessageResponse { Message = "Variant not found." });
        if (await SkuExists(request.Sku.Trim(), id))
            return Conflict(new MessageResponse { Message = "SKU already exists." });

        variant.Model = request.Model.Trim();
        variant.Sku = request.Sku.Trim();
        variant.Price = request.Price;
        variant.Stock = request.Stock;
        await _context.SaveChangesAsync();
        return Ok(ToDto(variant));
    }

    private Task<bool> SkuExists(string sku, int? excludeId) =>
        _context.Variants.AnyAsync(v => v.Sku.ToLower() == sku.ToLower() && v.Id != excludeId);
}
