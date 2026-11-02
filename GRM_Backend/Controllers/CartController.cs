using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize(Roles = "Customer")]
public class CartController : ControllerBase
{
    private readonly AppDbContext _context;

    public CartController(AppDbContext context)
    {
        _context = context;
    }

    private int UserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<CartDto> BuildCart()
    {
        var uid = UserId;
        var items = await _context.CartItems
            .Include(c => c.Variant).ThenInclude(v => v!.Product)
            .Where(c => c.UserId == uid).OrderBy(c => c.Id).ToListAsync();
        var dto = new CartDto();
        foreach (var c in items)
        {
            var v = c.Variant!;
            dto.Items.Add(new CartItemDto
            {
                VariantId = v.Id,
                ProductId = v.ProductId,
                ProductName = v.Product?.Name ?? string.Empty,
                Model = v.Model,
                Sku = v.Sku,
                UnitPrice = v.Price,
                Quantity = c.Quantity,
                Stock = v.Stock,
                LineTotal = v.Price * c.Quantity
            });
        }
        dto.TotalQuantity = dto.Items.Sum(i => i.Quantity);
        dto.Total = dto.Items.Sum(i => i.LineTotal);
        return dto;
    }

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await BuildCart());

    [HttpPost("items")]
    public async Task<IActionResult> Add([FromBody] AddCartItemRequest request)
    {
        var variant = await _context.Variants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == request.VariantId);
        if (variant == null || variant.Product is { IsActive: false })
            return NotFound(new MessageResponse { Message = "Variant not found." });

        var uid = UserId;
        var item = await _context.CartItems.FirstOrDefaultAsync(c => c.UserId == uid && c.VariantId == request.VariantId);
        var newQty = (item?.Quantity ?? 0) + request.Quantity;
        if (newQty > variant.Stock)
            return BadRequest(new MessageResponse { Message = "Requested quantity exceeds available stock." });

        if (item == null)
            _context.CartItems.Add(new CartItem { UserId = uid, VariantId = request.VariantId, Quantity = newQty });
        else
            item.Quantity = newQty;
        await _context.SaveChangesAsync();
        return Ok(await BuildCart());
    }

    [HttpPut("items/{variantId}")]
    public async Task<IActionResult> Update(int variantId, [FromBody] UpdateCartItemRequest request)
    {
        var uid = UserId;
        var item = await _context.CartItems.Include(c => c.Variant)
            .FirstOrDefaultAsync(c => c.UserId == uid && c.VariantId == variantId);
        if (item == null) return NotFound(new MessageResponse { Message = "Item not in cart." });
        if (request.Quantity > item.Variant!.Stock)
            return BadRequest(new MessageResponse { Message = "Requested quantity exceeds available stock." });
        item.Quantity = request.Quantity;
        await _context.SaveChangesAsync();
        return Ok(await BuildCart());
    }

    [HttpDelete("items/{variantId}")]
    public async Task<IActionResult> Remove(int variantId)
    {
        var uid = UserId;
        var item = await _context.CartItems.FirstOrDefaultAsync(c => c.UserId == uid && c.VariantId == variantId);
        if (item == null) return NotFound(new MessageResponse { Message = "Item not in cart." });
        _context.CartItems.Remove(item);
        await _context.SaveChangesAsync();
        return Ok(await BuildCart());
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var uid = UserId;
        _context.CartItems.RemoveRange(_context.CartItems.Where(c => c.UserId == uid));
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
