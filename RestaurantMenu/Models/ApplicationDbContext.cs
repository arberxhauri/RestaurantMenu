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

        // Decimal precision for Price
        builder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
