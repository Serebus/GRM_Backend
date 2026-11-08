using System.ComponentModel.DataAnnotations;

namespace GRM_Backend.DTOs;

// ── Supplier ──────────────────────────────────────────────────────────────────

public class UpsertSupplierRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    [EmailAddress] public string? Email { get; set; }
    public string? Phone   { get; set; }
    public string? Address { get; set; }
    public string? Notes   { get; set; }
    public bool IsActive   { get; set; } = true;
}

public class SupplierDto
{
    public int     Id            { get; set; }
    public string  Name          { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email         { get; set; }
    public string? Phone         { get; set; }
    public string? Address       { get; set; }
    public string? Notes         { get; set; }
    public bool    IsActive      { get; set; }
    public DateTime  CreatedAt   { get; set; }
    public DateTime? UpdatedAt   { get; set; }
}

// ── Requisition ───────────────────────────────────────────────────────────────

public class CreateRequisitionRequest
{
    [Required, MinLength(1)]
    public List<RequisitionItemRequest> Items { get; set; } = new();
    public string? Notes { get; set; }
}

public class RequisitionItemRequest
{
    [Required] public int VariantId { get; set; }
    [Range(1, int.MaxValue)] public int RequestedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class ReviewRequisitionRequest
{
    public string? ReviewNote { get; set; }
}

public class RequisitionItemDto
{
    public int    Id                { get; set; }
    public int    VariantId         { get; set; }
    public string Sku               { get; set; } = string.Empty;
    public string ProductName       { get; set; } = string.Empty;
    public string Model             { get; set; } = string.Empty;
    public int    RequestedQuantity { get; set; }
    public string? Notes            { get; set; }
}

public class RequisitionDto
{
    public int     Id           { get; set; }
    public int     StaffId      { get; set; }
    public string  StaffName    { get; set; } = string.Empty;
    public string  Status       { get; set; } = string.Empty;
    public string? Notes        { get; set; }
    public int?    ReviewedById { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ReviewNote   { get; set; }
    public DateTime  CreatedAt  { get; set; }
    public DateTime? UpdatedAt  { get; set; }
    public List<RequisitionItemDto> Items { get; set; } = new();
}

// ── Reorder ───────────────────────────────────────────────────────────────────

public class ReorderSuggestionDto
{
    public int     VariantId          { get; set; }
    public string  Sku                { get; set; } = string.Empty;
    public string  ProductName        { get; set; } = string.Empty;
    public string  Model              { get; set; } = string.Empty;
    public int     CurrentStock       { get; set; }
    public int     ReorderThreshold   { get; set; }
    public decimal DailyConsumption   { get; set; }
    public decimal DaysOfStockLeft    { get; set; }
    public int     SuggestedOrderQty  { get; set; }
    public int     VelocityWindowDays { get; set; }
}

public class UpdateReorderSettingRequest
{
    public int? ThresholdOverride      { get; set; }
    [Range(1, int.MaxValue)] public int DefaultOrderQuantity { get; set; } = 10;
    [Range(1, 365)] public int VelocityWindowDays { get; set; } = 30;
}

public class ReorderSettingDto
{
    public int    VariantId            { get; set; }
    public string Sku                  { get; set; } = string.Empty;
    public string ProductName          { get; set; } = string.Empty;
    public int?   ThresholdOverride    { get; set; }
    public int    DefaultOrderQuantity { get; set; }
    public int    VelocityWindowDays   { get; set; }
    public DateTime UpdatedAt          { get; set; }
    public string? UpdatedByName       { get; set; }
}

// ── Purchase Orders ───────────────────────────────────────────────────────────

public class CreatePurchaseOrderRequest
{
    public int? SupplierId    { get; set; }
    public int? RequisitionId { get; set; }

    [Required, MinLength(1)]
    public List<PoItemRequest> Items { get; set; } = new();
    public string? Notes { get; set; }
}

public class PoItemRequest
{
    [Required] public int VariantId { get; set; }
    [Range(1, int.MaxValue)] public int OrderedQuantity { get; set; }
    [Range(0, double.MaxValue)] public decimal UnitCost { get; set; }
}

public class UpdatePurchaseOrderRequest
{
    public int? SupplierId { get; set; }
    public List<PoItemRequest>? Items { get; set; }
    public string? Notes { get; set; }
}

public class ReceivePurchaseOrderRequest
{
    [Required, MinLength(1)]
    public List<PoReceiveLineRequest> Lines { get; set; } = new();
}

public class PoReceiveLineRequest
{
    [Required] public int PurchaseOrderItemId { get; set; }
    [Range(0, int.MaxValue)] public int ReceivedQuantity { get; set; }
}

public class PurchaseOrderItemDto
{
    public int     Id               { get; set; }
    public int     VariantId        { get; set; }
    public string  Sku              { get; set; } = string.Empty;
    public string  ProductName      { get; set; } = string.Empty;
    public string  Model            { get; set; } = string.Empty;
    public int     OrderedQuantity  { get; set; }
    public int     ReceivedQuantity { get; set; }
    public decimal UnitCost         { get; set; }
    public decimal LineTotal        { get; set; }
}

public class PurchaseOrderDto
{
    public int     Id              { get; set; }
    public int?    SupplierId      { get; set; }
    public string? SupplierName    { get; set; }
    public int?    RequisitionId   { get; set; }
    public string  Status          { get; set; } = string.Empty;
    public string? Notes           { get; set; }
    public int     CreatedById     { get; set; }
    public string  CreatedByName   { get; set; } = string.Empty;
    public int?    ApprovedById    { get; set; }
    public string? ApprovedByName  { get; set; }
    public decimal TotalCost       { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public DateTime? UpdatedAt     { get; set; }
    public DateTime? ApprovedAt    { get; set; }
    public DateTime? SentAt        { get; set; }
    public DateTime? ReceivedAt    { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}
