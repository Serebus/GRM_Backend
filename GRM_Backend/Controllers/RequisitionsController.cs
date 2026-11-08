using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController : ControllerBase
{
    private readonly AppDbContext _context;
    public RequisitionsController(AppDbContext context) => _context = context;

    private int    CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole   => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    private static RequisitionDto ToDto(Requisition r) => new()
    {
        Id              = r.Id,
        StaffId         = r.StaffId,
        StaffName       = r.Staff?.FullName ?? r.Staff?.Username ?? string.Empty,
        Status          = r.Status,
        Notes           = r.Notes,
        ReviewedById    = r.ReviewedById,
        ReviewedByName  = r.ReviewedBy?.FullName ?? r.ReviewedBy?.Username,
        ReviewNote      = r.ReviewNote,
        CreatedAt       = r.CreatedAt,
        UpdatedAt       = r.UpdatedAt,
        Items = r.Items.Select(i => new RequisitionItemDto
        {
            Id                = i.Id,
            VariantId         = i.VariantId,
            Sku               = i.Variant?.Sku ?? string.Empty,
            ProductName       = i.Variant?.Product?.Name ?? string.Empty,
            Model             = i.Variant?.Model ?? string.Empty,
            RequestedQuantity = i.RequestedQuantity,
            Notes             = i.Notes
        }).ToList()
    };

    private async Task<Requisition?> FindRequisition(int id) =>
        await _context.Requisitions
            .Include(r => r.Staff)
            .Include(r => r.ReviewedBy)
            .Include(r => r.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(r => r.Id == id);

    // POST /api/requisitions — Staff submits a restock request
    [HttpPost]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Create([FromBody] CreateRequisitionRequest request)
    {
        var items = new List<RequisitionItem>();
        foreach (var r in request.Items)
        {
            var variant = await _context.Variants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == r.VariantId);
            if (variant == null || variant.Product is { IsActive: false })
                return NotFound(new MessageResponse { Message = $"Variant {r.VariantId} not found or inactive." });

            items.Add(new RequisitionItem
            {
                VariantId         = variant.Id,
                RequestedQuantity = r.RequestedQuantity,
                Notes             = r.Notes?.Trim()
            });
        }

        var req = new Requisition
        {
            StaffId = CurrentUserId,
            Notes   = request.Notes?.Trim(),
            Items   = items
        };

        _context.Requisitions.Add(req);
        await _context.SaveChangesAsync();

        var loaded = (await FindRequisition(req.Id))!;
        return CreatedAtAction(nameof(GetById), new { id = req.Id }, ToDto(loaded));
    }

    // GET /api/requisitions — Staff: own; Admin: all
    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetAll()
    {
        var query = _context.Requisitions
            .Include(r => r.Staff)
            .Include(r => r.ReviewedBy)
            .Include(r => r.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product)
            .AsQueryable();

        if (CurrentRole == "Staff")
            query = query.Where(r => r.StaffId == CurrentUserId);

        var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(list.Select(ToDto));
    }

    // GET /api/requisitions/{id}
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<IActionResult> GetById(int id)
    {
        var req = await FindRequisition(id);
        if (req == null) return NotFound(new MessageResponse { Message = "Requisition not found." });

        if (CurrentRole == "Staff" && req.StaffId != CurrentUserId)
            return Forbid();

        return Ok(ToDto(req));
    }

    // POST /api/requisitions/{id}/approve — Admin
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(int id, [FromBody] ReviewRequisitionRequest? body)
    {
        var req = await FindRequisition(id);
        if (req == null) return NotFound(new MessageResponse { Message = "Requisition not found." });

        if (req.Status != RequisitionStatus.Pending)
            return BadRequest(new MessageResponse { Message = "Only Pending requisitions can be approved." });

        req.Status       = RequisitionStatus.Approved;
        req.ReviewedById = CurrentUserId;
        req.ReviewNote   = body?.ReviewNote?.Trim();
        req.UpdatedAt    = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(req));
    }

    // POST /api/requisitions/{id}/decline — Admin
    [HttpPost("{id:int}/decline")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Decline(int id, [FromBody] ReviewRequisitionRequest? body)
    {
        var req = await FindRequisition(id);
        if (req == null) return NotFound(new MessageResponse { Message = "Requisition not found." });

        if (req.Status != RequisitionStatus.Pending)
            return BadRequest(new MessageResponse { Message = "Only Pending requisitions can be declined." });

        req.Status       = RequisitionStatus.Declined;
        req.ReviewedById = CurrentUserId;
        req.ReviewNote   = body?.ReviewNote?.Trim();
        req.UpdatedAt    = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(req));
    }
}
