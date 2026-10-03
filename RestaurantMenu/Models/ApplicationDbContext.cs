using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RestaurantMenu.Models;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Branch> Branches { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<MenuEvent> MenuEvents { get; set; }
    public DbSet<BranchHours> BranchHours { get; set; }
    public DbSet<ProductOptionGroup> ProductOptionGroups { get; set; }
    public DbSet<ProductOption> ProductOptions { get; set; }
    public DbSet<BranchMember> BranchMembers { get; set; }
    public DbSet<DiningTable> Tables { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure relationships
        builder.Entity<Branch>()
            .HasOne(b => b.User)
            .WithMany(u => u.Branches)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Category>()
            .HasOne(c => c.Branch)
            .WithMany(b => b.Categories)
            .HasForeignKey(c => c.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft delete query filters
        builder.Entity<ApplicationUser>()
            .HasQueryFilter(u => !u.IsDeleted);

        builder.Entity<Branch>()
            .HasQueryFilter(b => !b.IsDeleted);

        // Deleting is a soft delete (SoftDeleteInterceptor), so a category or dish is
        // only hidden and can be restored. Deleting a category also deletes its dishes.
        builder.Entity<Category>()
            .HasQueryFilter(c => !c.IsDeleted);

        builder.Entity<Product>()
            .HasQueryFilter(p => !p.IsDeleted);

        // Unique among live branches only: a deleted branch must not block its name,
        // since the name is also the menu URL.
        builder.Entity<Branch>()
            .HasIndex(b => b.Name)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Analytics: every report reads one branch over a date range. No foreign keys on
        // purpose: events outlive soft-deleted dishes, and an insert must never fail
        // because of the state of the menu.
        builder.Entity<MenuEvent>(e =>
        {
            e.HasIndex(x => new { x.BranchId, x.CreatedUtc });
            e.HasIndex(x => x.CreatedUtc); // retention cleanup
            e.Property(x => x.Lang).HasMaxLength(8);
        });

        builder.Entity<BranchHours>(e =>
        {
            e.HasOne(h => h.Branch)
                .WithMany(b => b.OpeningHours)
                .HasForeignKey(h => h.BranchId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(h => new { h.BranchId, h.DayOfWeek });
            // Matches the Branch filter: a deleted branch's hours are never read on their own.
            e.HasQueryFilter(h => !h.Branch!.IsDeleted);
        });

        builder.Entity<Branch>()
            .Property(b => b.TimeZone)
            .HasMaxLength(64)
            .HasDefaultValue("Europe/Tirane");

        // Dish options: deleted with their dish (hard delete; a soft-deleted dish keeps them
        // for Undo). Filters match the Product filter.
        builder.Entity<ProductOptionGroup>(e =>
        {
            e.HasOne(g => g.Product).WithMany(p => p.OptionGroups).HasForeignKey(g => g.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.Property(g => g.Name).HasMaxLength(60);
            e.HasQueryFilter(g => !g.Product!.IsDeleted);
        });
        builder.Entity<ProductOption>(e =>
        {
            e.HasOne(o => o.Group).WithMany(g => g.Options).HasForeignKey(o => o.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.Property(o => o.Name).HasMaxLength(60);
            e.Property(o => o.PriceDelta).HasPrecision(18, 2);
            e.HasQueryFilter(o => !o.Group!.Product!.IsDeleted);
        });

        // Branch staff: one membership per person per branch. Removed with the branch row
        // (soft-deleted branches keep them for Undo; the filter hides them meanwhile).
        builder.Entity<BranchMember>(e =>
        {
            e.HasIndex(m => new { m.BranchId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne(m => m.Branch).WithMany(b => b.Members).HasForeignKey(m => m.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(m => !m.Branch!.IsDeleted && !m.User!.IsDeleted);
        });

        // Table ordering. Tables and orders follow their branch's soft delete. Orders keep
        // copies of everything they show, so dishes and tables may change or go.
        builder.Entity<DiningTable>(e =>
        {
            e.HasIndex(t => new { t.BranchId, t.Number }).IsUnique();
            e.HasOne(t => t.Branch).WithMany(b => b.Tables).HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Name).HasMaxLength(40);
            e.Property(t => t.Code).HasMaxLength(12);
            e.HasQueryFilter(t => !t.Branch!.IsDeleted);
        });
        builder.Entity<Order>(e =>
        {
            e.HasIndex(o => o.PublicId).IsUnique();
            e.HasIndex(o => new { o.BranchId, o.ClientRequestId }).IsUnique();
            e.HasIndex(o => new { o.BranchId, o.OrderDay, o.Number }).IsUnique();
            e.HasIndex(o => new { o.BranchId, o.Status, o.CreatedUtc });
            e.HasIndex(o => o.CreatedUtc); // retention cleanup
            e.HasOne(o => o.Branch).WithMany().HasForeignKey(o => o.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<DiningTable>().WithMany().HasForeignKey(o => o.TableId).OnDelete(DeleteBehavior.SetNull);
            e.Property(o => o.TableName).HasMaxLength(40);
            e.Property(o => o.Note).HasMaxLength(200);
            e.Property(o => o.Language).HasMaxLength(8);
            e.Property(o => o.Total).HasPrecision(18, 2);
            e.HasQueryFilter(o => !o.Branch!.IsDeleted);
        });
        builder.Entity<OrderItem>(e =>
        {
            e.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.Property(i => i.Name).HasMaxLength(200);
            e.Property(i => i.Options).HasMaxLength(500);
            e.Property(i => i.OptionIds).HasMaxLength(200);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.HasQueryFilter(i => !i.Order!.Branch!.IsDeleted);
        });

        // Decimal precision for Price
        builder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
