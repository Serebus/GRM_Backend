using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>POST /api/quotations/requests  (Customer)</summary>
public class QuoteRequestBody
{
    [Required, MinLength(1)]
    public List<QuoteItemRequest> Items { get; set; } = new();

    public string? Notes { get; set; }
}

/// <summary>POST /api/quotations  (Staff / Admin)</summary>
public class CreateQuotationRequest
{
    [Required]
    public int CustomerId { get; set; }

    [Required, MinLength(1)]
    public List<QuoteItemRequest> Items { get; set; } = new();

    [Range(0, 100)]
    public decimal DiscountPercent { get; set; } = 0;

    public string? Notes { get; set; }
}

public class QuoteItemRequest
{
    [Required]
    public int VariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

/// <summary>PUT /api/quotations/{id}  (Staff / Admin, only while Draft)</summary>
public class UpdateQuotationRequest
{
    public List<QuoteItemRequest>? Items { get; set; }

    [Range(0, 100)]
    public decimal? DiscountPercent { get; set; }

    public string? Notes { get; set; }
}

/// <summary>POST /api/quotations/{id}/accept or /decline  (Customer)</summary>
public class RespondQuotationRequest
{
    /// <summary>Required only for decline; optional message for accept.</summary>
    public string? Reason { get; set; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class QuotationItemDto
{
    public int Id { get; set; }
    public int VariantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class QuotationDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? StaffId { get; set; }
    public string? StaffName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? DeclineReason { get; set; }
    public List<QuotationItemDto> Items { get; set; } = new();
}

public class SalesOrderDto
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}
