using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize(Roles = "Staff,Admin")]
public class InventoryController : ControllerBase
{
    private readonly AppDbContext _context;

    public InventoryController(AppDbContext context)
    {
        _context = context;
    }

    private static InventoryItemDto ToDto(ProductVariant v) => new()
    {
        VariantId = v.Id,
        ProductId = v.ProductId,
        ProductName = v.Product?.Name ?? string.Empty,
        Model = v.Model,
        Sku = v.Sku,
        Stock = v.Stock,
        ReorderThreshold = v.ReorderThreshold
    };

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var variants = await _context.Variants.Include(v => v.Product).OrderBy(v => v.Id).ToListAsync();
        return Ok(variants.Select(ToDto));
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock()
    {
        var variants = await _context.Variants.Include(v => v.Product)
            .Where(v => v.Stock < v.ReorderThreshold).OrderBy(v => v.Stock).ToListAsync();
        return Ok(variants.Select(ToDto));
    }

    [HttpPost("stock-in")]
    public async Task<IActionResult> StockIn([FromBody] StockInRequest request)
    {
        var variant = await _context.Variants.FindAsync(request.VariantId);
        if (variant == null) return NotFound(new MessageResponse { Message = "Variant not found." });
        return await Apply(variant, "StockIn", request.Quantity, request.Reason);
    }

    [HttpPost("stock-out")]
    public async Task<IActionResult> StockOut([FromBody] StockOutRequest request)
    {
        var variant = await _context.Variants.FindAsync(request.VariantId);
        if (variant == null) return NotFound(new MessageResponse { Message = "Variant not found." });
        if (variant.Stock < request.Quantity)
            return BadRequest(new MessageResponse { Message = "Insufficient stock." });
        return await Apply(variant, "StockOut", -request.Quantity, request.Reason);
    }

    [HttpPost("adjustments")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Adjust([FromBody] StockAdjustmentRequest request)
    {
        var variant = await _context.Variants.FindAsync(request.VariantId);
        if (variant == null) return NotFound(new MessageResponse { Message = "Variant not found." });
        return await Apply(variant, "Adjustment", request.NewStock - variant.Stock, request.Reason);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] int? variantId)
    {
        var query = _context.StockMovements.Include(m => m.Variant).AsQueryable();
        if (variantId.HasValue) query = query.Where(m => m.VariantId == variantId.Value);
        var list = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync();
        return Ok(list.Select(m => new StockMovementDto
        {
            Id = m.Id,
            VariantId = m.VariantId,
            Sku = m.Variant?.Sku ?? string.Empty,
            Type = m.Type,
            QuantityChange = m.QuantityChange,
            StockAfter = m.StockAfter,
            Reason = m.Reason,
            UserId = m.UserId,
            CreatedAt = m.CreatedAt
        }));
    }

    private async Task<IActionResult> Apply(ProductVariant variant, string type, int change, string? reason)
    {
        variant.Stock += change;
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid);
        _context.StockMovements.Add(new StockMovement
        {
            VariantId = variant.Id,
            Type = type,
            QuantityChange = change,
            StockAfter = variant.Stock,
            Reason = reason?.Trim(),
            UserId = uid == 0 ? null : uid
        });
        await _context.SaveChangesAsync();
        return Ok(new { variantId = variant.Id, sku = variant.Sku, stock = variant.Stock, change, type });
    }
}
