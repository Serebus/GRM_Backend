using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>POST /api/orders  — Customer checkout from cart (online order).</summary>
public class CheckoutRequest
{
    /// <summary>Optional override list. When null/empty the customer's active cart is used.</summary>
    public List<OrderItemRequest>? Items { get; set; }

    public string? Notes { get; set; }
}

/// <summary>POST /api/orders/walk-in  — Staff / Admin in-store sale.</summary>
public class WalkInOrderRequest
{
    [Required]
    public int CustomerId { get; set; }

    [Required, MinLength(1)]
    public List<OrderItemRequest> Items { get; set; } = new();

    public string? Notes { get; set; }
}

public class OrderItemRequest
{
    [Required]
    public int VariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

/// <summary>PATCH /api/orders/{id}/status  — Staff / Admin status transition.</summary>
public class UpdateOrderStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

/// <summary>PUT /api/orders/{id}/correction  — Admin corrects a finalized order.</summary>
public class OrderCorrectionRequest
{
    public List<OrderItemRequest>? Items { get; set; }

    [Required, MinLength(1)]
    public string CorrectionNote { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

// ── Query parameters ──────────────────────────────────────────────────────────

public class OrderListQuery
{
    /// <summary>Filter by status: Pending | Processing | Completed | Cancelled</summary>
    public string? Status { get; set; }

    /// <summary>Filter by channel: Online | WalkIn</summary>
    public string? Channel { get; set; }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class OrderItemDto
{
    public int     Id          { get; set; }
    public int     VariantId   { get; set; }
    public string  Sku         { get; set; } = string.Empty;
    public string  ProductName { get; set; } = string.Empty;
    public string  Model       { get; set; } = string.Empty;
    public int     Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
    public decimal LineTotal   { get; set; }
}

public class OrderDto
{
    public int     Id             { get; set; }
    public int     CustomerId     { get; set; }
    public string  CustomerName   { get; set; } = string.Empty;
    public int?    StaffId        { get; set; }
    public string? StaffName      { get; set; }
    public string  Status         { get; set; } = string.Empty;
    public string  Channel        { get; set; } = string.Empty;
    public string? Notes          { get; set; }
    public string? CorrectionNote { get; set; }
    public decimal Subtotal       { get; set; }
    public decimal Total          { get; set; }
    public DateTime  CreatedAt    { get; set; }
    public DateTime? UpdatedAt    { get; set; }
    public DateTime? CancelledAt  { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
