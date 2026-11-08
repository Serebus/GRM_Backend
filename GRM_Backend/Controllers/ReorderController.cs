using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/reorder")]
[Authorize(Roles = "Admin")]
public class ReorderController : ControllerBase
{
    private readonly AppDbContext _context;
    public ReorderController(AppDbContext context) => _context = context;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Shared computation ────────────────────────────────────────────────────

    /// <summary>
    /// Computes reorder suggestions for all variants whose effective stock ≤ threshold.
    /// Uses StockOut movements within the configured velocity window.
    /// </summary>
    private async Task<List<ReorderSuggestionDto>> ComputeSuggestions()
    {
        var variants = await _context.Variants
            .Include(v => v.Product)
            .Where(v => v.Product != null && v.Product.IsActive)
            .ToListAsync();

        var settings = await _context.ReorderSettings
            .ToDictionaryAsync(s => s.VariantId);

        var now = DateTime.UtcNow;
        var suggestions = new List<ReorderSuggestionDto>();

        foreach (var variant in variants)
        {
            settings.TryGetValue(variant.Id, out var setting);

            var windowDays  = setting?.VelocityWindowDays ?? 30;
            var threshold   = setting?.ThresholdOverride  ?? variant.ReorderThreshold;
            var defaultQty  = setting?.DefaultOrderQuantity ?? Math.Max(variant.ReorderThreshold * 2, 10);
            var windowStart = now.AddDays(-windowDays);

            // Total units sold in the window
            var totalOut = await _context.StockMovements
                .Where(m => m.VariantId == variant.Id
                         && m.Type == "StockOut"
                         && m.CreatedAt >= windowStart)
                .SumAsync(m => (int?)(-m.QuantityChange)) ?? 0;  // QuantityChange is negative for StockOut

            var dailyConsumption = windowDays > 0 ? (decimal)totalOut / windowDays : 0;
            var daysLeft = dailyConsumption > 0
                ? Math.Round((decimal)variant.Stock / dailyConsumption, 1)
                : (variant.Stock > 0 ? 999m : 0m);

            // Only surface variants that are at or below threshold
            if (variant.Stock > threshold) continue;

            suggestions.Add(new ReorderSuggestionDto
            {
                VariantId         = variant.Id,
                Sku               = variant.Sku,
                ProductName       = variant.Product?.Name ?? string.Empty,
                Model             = variant.Model,
                CurrentStock      = variant.Stock,
                ReorderThreshold  = threshold,
                DailyConsumption  = Math.Round(dailyConsumption, 2),
                DaysOfStockLeft   = daysLeft,
                SuggestedOrderQty = defaultQty,
                VelocityWindowDays = windowDays
            });
        }

        return suggestions.OrderBy(s => s.DaysOfStockLeft).ToList();
    }

    // GET /api/reorder/suggestions
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions() =>
        Ok(await ComputeSuggestions());

    // POST /api/reorder/recalculate  — force recompute (same logic, explicit trigger)
    [HttpPost("recalculate")]
    public async Task<IActionResult> Recalculate() =>
        Ok(new { recalculatedAt = DateTime.UtcNow, suggestions = await ComputeSuggestions() });

    // PUT /api/reorder/settings/{variantId}
    [HttpPut("settings/{variantId:int}")]
    public async Task<IActionResult> UpsertSettings(int variantId, [FromBody] UpdateReorderSettingRequest request)
    {
        var variant = await _context.Variants.Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId);
        if (variant == null) return NotFound(new MessageResponse { Message = "Variant not found." });

        var setting = await _context.ReorderSettings.FirstOrDefaultAsync(s => s.VariantId == variantId);
        if (setting == null)
        {
            setting = new ReorderSetting { VariantId = variantId };
            _context.ReorderSettings.Add(setting);
        }

        setting.ThresholdOverride    = request.ThresholdOverride;
        setting.DefaultOrderQuantity = request.DefaultOrderQuantity;
        setting.VelocityWindowDays   = request.VelocityWindowDays;
        setting.UpdatedById          = CurrentUserId;
        setting.UpdatedAt            = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _context.Entry(setting).Reference(s => s.UpdatedBy).LoadAsync();

        return Ok(new ReorderSettingDto
        {
            VariantId            = setting.VariantId,
            Sku                  = variant.Sku,
            ProductName          = variant.Product?.Name ?? string.Empty,
            ThresholdOverride    = setting.ThresholdOverride,
            DefaultOrderQuantity = setting.DefaultOrderQuantity,
            VelocityWindowDays   = setting.VelocityWindowDays,
            UpdatedAt            = setting.UpdatedAt,
            UpdatedByName        = setting.UpdatedBy?.FullName ?? setting.UpdatedBy?.Username
        });
    }
}
