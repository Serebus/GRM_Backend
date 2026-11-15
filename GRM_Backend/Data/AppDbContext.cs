using Microsoft.EntityFrameworkCore;
using GRM_Backend.Models;

namespace GRM_Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<BundleItem> BundleItems => Set<BundleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Requisition> Requisitions => Set<Requisition>();
    public DbSet<RequisitionItem> RequisitionItems => Set<RequisitionItem>();
    public DbSet<ReorderSetting> ReorderSettings => Set<ReorderSetting>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<Product>()
            .HasMany(p => p.Variants).WithOne(v => v.Product).HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProductVariant>().HasIndex(v => v.Sku).IsUnique();
        modelBuilder.Entity<ProductVariant>().Property(v => v.Price).HasPrecision(18, 2);
        modelBuilder.Entity<ProductVariant>().Property(v => v.CostPrice).HasPrecision(18, 2);
        modelBuilder.Entity<StockMovement>()
            .HasOne(m => m.Variant).WithMany().HasForeignKey(m => m.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CartItem>()
            .HasOne(c => c.Variant).WithMany().HasForeignKey(c => c.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CartItem>().HasIndex(c => new { c.UserId, c.VariantId }).IsUnique();
        modelBuilder.Entity<Bundle>().Property(b => b.Price).HasPrecision(18, 2);
        modelBuilder.Entity<Bundle>()
            .HasMany(b => b.Items).WithOne().HasForeignKey(i => i.BundleId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BundleItem>()
            .HasOne(i => i.Variant).WithMany().HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Quotation
        modelBuilder.Entity<Quotation>()
            .HasOne(q => q.Customer).WithMany().HasForeignKey(q => q.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Quotation>()
            .HasOne(q => q.Staff).WithMany().HasForeignKey(q => q.StaffId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Quotation>()
            .Property(q => q.DiscountPercent).HasPrecision(5, 2);
        modelBuilder.Entity<Quotation>()
            .HasMany(q => q.Items).WithOne(i => i.Quotation).HasForeignKey(i => i.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);

        // QuotationItem
        modelBuilder.Entity<QuotationItem>()
            .HasOne(i => i.Variant).WithMany().HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<QuotationItem>()
            .Property(i => i.UnitPrice).HasPrecision(18, 2);

        // SalesOrder
        modelBuilder.Entity<SalesOrder>()
            .HasOne(o => o.Quotation).WithMany().HasForeignKey(o => o.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SalesOrder>()
            .HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SalesOrder>()
            .Property(o => o.Total).HasPrecision(18, 2);
        modelBuilder.Entity<SalesOrder>()
            .HasIndex(o => o.QuotationId).IsUnique(); // one order per quote

        // Order
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Staff).WithMany().HasForeignKey(o => o.StaffId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Order>()
            .HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // OrderItem
        modelBuilder.Entity<OrderItem>()
            .HasOne(i => i.Variant).WithMany().HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItem>()
            .Property(i => i.UnitPrice).HasPrecision(18, 2);

        // Invoice
        modelBuilder.Entity<Invoice>()
            .HasOne(inv => inv.Order).WithMany().HasForeignKey(inv => inv.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(inv => inv.Customer).WithMany().HasForeignKey(inv => inv.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasOne(inv => inv.IssuedBy).WithMany().HasForeignKey(inv => inv.IssuedById)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invoice>()
            .HasMany(inv => inv.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invoice>()
            .Property(inv => inv.AmountDue).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>()
            .Property(inv => inv.AmountPaid).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>()
            .HasIndex(inv => inv.OrderId); // one order can have one active invoice

        // InvoiceLine
        modelBuilder.Entity<InvoiceLine>()
            .Property(l => l.UnitPrice).HasPrecision(18, 2);

        // Supplier — no extra config needed beyond conventions

        // Requisition
        modelBuilder.Entity<Requisition>()
            .HasOne(r => r.Staff).WithMany().HasForeignKey(r => r.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Requisition>()
            .HasOne(r => r.ReviewedBy).WithMany().HasForeignKey(r => r.ReviewedById)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Requisition>()
            .HasMany(r => r.Items).WithOne(i => i.Requisition).HasForeignKey(i => i.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // RequisitionItem
        modelBuilder.Entity<RequisitionItem>()
            .HasOne(i => i.Variant).WithMany().HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ReorderSetting — one per variant
        modelBuilder.Entity<ReorderSetting>()
            .HasOne(s => s.Variant).WithMany().HasForeignKey(s => s.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ReorderSetting>()
            .HasOne(s => s.UpdatedBy).WithMany().HasForeignKey(s => s.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<ReorderSetting>()
            .HasIndex(s => s.VariantId).IsUnique();

        // PurchaseOrder
        modelBuilder.Entity<PurchaseOrder>()
            .HasOne(po => po.Supplier).WithMany().HasForeignKey(po => po.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PurchaseOrder>()
            .HasOne(po => po.Requisition).WithMany().HasForeignKey(po => po.RequisitionId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PurchaseOrder>()
            .HasOne(po => po.CreatedBy).WithMany().HasForeignKey(po => po.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PurchaseOrder>()
            .HasOne(po => po.ApprovedBy).WithMany().HasForeignKey(po => po.ApprovedById)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PurchaseOrder>()
            .HasMany(po => po.Items).WithOne(i => i.PurchaseOrder).HasForeignKey(i => i.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // PurchaseOrderItem
        modelBuilder.Entity<PurchaseOrderItem>()
            .HasOne(i => i.Variant).WithMany().HasForeignKey(i => i.VariantId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PurchaseOrderItem>()
            .Property(i => i.UnitCost).HasPrecision(18, 2);

        // RefreshToken
        modelBuilder.Entity<RefreshToken>()
            .HasOne(r => r.User).WithMany(u => u.RefreshTokens).HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(r => r.Token).IsUnique();

        // RevokedToken
        modelBuilder.Entity<RevokedToken>()
            .HasIndex(t => t.Token);
    }
}
