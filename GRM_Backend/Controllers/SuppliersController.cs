using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Roles = "Admin")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _context;
    public SuppliersController(AppDbContext context) => _context = context;

    private static SupplierDto ToDto(Supplier s) => new()
    {
        Id            = s.Id,
        Name          = s.Name,
        ContactPerson = s.ContactPerson,
        Email         = s.Email,
        Phone         = s.Phone,
        Address       = s.Address,
        Notes         = s.Notes,
        IsActive      = s.IsActive,
        CreatedAt     = s.CreatedAt,
        UpdatedAt     = s.UpdatedAt
    };

    // GET /api/suppliers
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? activeOnly)
    {
        var query = _context.Suppliers.AsQueryable();
        if (activeOnly == true) query = query.Where(s => s.IsActive);
        var list = await query.OrderBy(s => s.Name).ToListAsync();
        return Ok(list.Select(ToDto));
    }

    // GET /api/suppliers/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _context.Suppliers.FindAsync(id);
        return s == null ? NotFound(new MessageResponse { Message = "Supplier not found." }) : Ok(ToDto(s));
    }

    // POST /api/suppliers
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertSupplierRequest request)
    {
        var supplier = new Supplier
        {
            Name          = request.Name.Trim(),
            ContactPerson = request.ContactPerson?.Trim(),
            Email         = request.Email?.Trim(),
            Phone         = request.Phone?.Trim(),
            Address       = request.Address?.Trim(),
            Notes         = request.Notes?.Trim(),
            IsActive      = request.IsActive
        };
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, ToDto(supplier));
    }

    // PUT /api/suppliers/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertSupplierRequest request)
    {
        var s = await _context.Suppliers.FindAsync(id);
        if (s == null) return NotFound(new MessageResponse { Message = "Supplier not found." });

        s.Name          = request.Name.Trim();
        s.ContactPerson = request.ContactPerson?.Trim();
        s.Email         = request.Email?.Trim();
        s.Phone         = request.Phone?.Trim();
        s.Address       = request.Address?.Trim();
        s.Notes         = request.Notes?.Trim();
        s.IsActive      = request.IsActive;
        s.UpdatedAt     = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(s));
    }

    // DELETE /api/suppliers/{id}  — soft-delete (deactivate)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _context.Suppliers.FindAsync(id);
        if (s == null) return NotFound(new MessageResponse { Message = "Supplier not found." });

        // Check if any active POs reference this supplier
        var hasPOs = await _context.PurchaseOrders
            .AnyAsync(po => po.SupplierId == id &&
                            po.Status != PurchaseOrderStatus.Received &&
                            po.Status != PurchaseOrderStatus.Cancelled);
        if (hasPOs)
            return Conflict(new MessageResponse
                { Message = "Supplier has active purchase orders. Deactivate them first." });

        s.IsActive  = false;
        s.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(ToDto(s));
    }
}
