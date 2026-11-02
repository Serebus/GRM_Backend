using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/bundles")]
[Authorize(Roles = "Admin")]
public class BundlesController : ControllerBase
{
    private readonly AppDbContext _context;

    public BundlesController(AppDbContext context) => _context = context;

    private static BundleDto ToDto(Bundle b) => new()
    {
        Id = b.Id, Name = b.Name, Description = b.Description, Price = b.Price,
        Items = b.Items.Select(i => new BundleItemDto
        {
            VariantId = i.VariantId, Sku = i.Variant?.Sku ?? "", Model = i.Variant?.Model ?? "", Quantity = i.Quantity
        }).ToList()
    };

    private IQueryable<Bundle> Query() => _context.Bundles.Include(b => b.Items).ThenInclude(i => i.Variant);

    private async Task<string?> ValidateItems(BundleRequest r)
    {
        if (r.Items.Select(i => i.VariantId).Distinct().Count() != r.Items.Count)
            return "Duplicate variants in bundle.";
        var ids = r.Items.Select(i => i.VariantId).ToList();
        var found = await _context.Variants.CountAsync(v => ids.Contains(v.Id));
        return found != ids.Count ? "One or more variants do not exist." : null;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<BundleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List() =>
        Ok((await Query().OrderBy(b => b.Id).ToListAsync()).Select(ToDto).ToList());

    [HttpPost]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] BundleRequest request)
    {
        var error = await ValidateItems(request);
        if (error != null) return BadRequest(new MessageResponse { Message = error });

        var bundle = new Bundle
        {
            Name = request.Name.Trim(), Description = request.Description?.Trim(), Price = request.Price,
            Items = request.Items.Select(i => new BundleItem { VariantId = i.VariantId, Quantity = i.Quantity }).ToList()
        };
        _context.Bundles.Add(bundle);
        await _context.SaveChangesAsync();
        var created = await Query().FirstAsync(b => b.Id == bundle.Id);
        return CreatedAtAction(nameof(List), ToDto(created));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] BundleRequest request)
    {
        var bundle = await Query().FirstOrDefaultAsync(b => b.Id == id);
        if (bundle == null) return NotFound(new MessageResponse { Message = "Bundle not found." });
        var error = await ValidateItems(request);
        if (error != null) return BadRequest(new MessageResponse { Message = error });

        bundle.Name = request.Name.Trim();
        bundle.Description = request.Description?.Trim();
        bundle.Price = request.Price;
        bundle.UpdatedAt = DateTime.UtcNow;
        _context.BundleItems.RemoveRange(bundle.Items);
        bundle.Items = request.Items.Select(i => new BundleItem { BundleId = id, VariantId = i.VariantId, Quantity = i.Quantity }).ToList();
        await _context.SaveChangesAsync();
        var updated = await Query().FirstAsync(b => b.Id == id);
        return Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var bundle = await Query().FirstOrDefaultAsync(b => b.Id == id);
        if (bundle == null) return NotFound(new MessageResponse { Message = "Bundle not found." });
        _context.Bundles.Remove(bundle);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
