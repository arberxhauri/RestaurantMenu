using RestaurantMenu.Helpers;
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
    public DbSet<BranchSlugAlias> BranchSlugAliases { get; set; }
    public DbSet<PriceBook> PriceBook { get; set; }
    public DbSet<PlanSettings> PlanSettings { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<SubscriptionItem> SubscriptionItems { get; set; }
    public DbSet<BillingProfile> BillingProfiles { get; set; }
    public DbSet<SubscriptionAudit> SubscriptionAudits { get; set; }
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
    public DbSet<ReservationSettings> ReservationSettings { get; set; }
    public DbSet<Reservation> Reservations { get; set; }
    public DbSet<BranchSite> BranchSites { get; set; }
    public DbSet<Domain> Domains { get; set; }
    public DbSet<Ingredient> Ingredients { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<DishIngredient> DishIngredients { get; set; }
    public DbSet<Shift> Shifts { get; set; }
    public DbSet<DailySales> DailySales { get; set; }
    public DbSet<Feedback> Feedback { get; set; }

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
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.SignupSource).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SignupSource.Admin);
            e.Property(u => u.Country).HasMaxLength(2).HasDefaultValue("AL");
            e.Property(u => u.Language).HasMaxLength(8).HasDefaultValue("en");
            e.Property(u => u.TermsVersion).HasMaxLength(20);
            e.Property(u => u.ApprovalNote).HasMaxLength(300);
            e.Property(u => u.SignupRestaurantName).HasMaxLength(100);
            // The admin's "waiting for approval" list and the unconfirmed-signup cleanup.
            e.HasIndex(u => new { u.SignupSource, u.ApprovedUtc });
        });

        builder.Entity<Branch>()
            .HasQueryFilter(b => !b.IsDeleted);

        // Deleting is a soft delete (SoftDeleteInterceptor), so a category or dish is
        // only hidden and can be restored. Deleting a category also deletes its dishes.
        builder.Entity<Category>()
            .HasQueryFilter(c => !c.IsDeleted);

        builder.Entity<Product>()
            .HasQueryFilter(p => !p.IsDeleted);

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

        // Menu links. Unique across every branch, deleted ones included (see Branch.Slug).
        // Nullable in the database for one deploy: rows are filled at startup, and a later
        // migration makes it NOT NULL.
        builder.Entity<Branch>(e =>
        {
            e.Property(b => b.Slug).HasMaxLength(SlugRules.MaxLength).IsRequired(false);
            e.HasIndex(b => b.Slug).IsUnique();
        });
        builder.Entity<BranchSlugAlias>(e =>
        {
            e.Property(a => a.Slug).HasMaxLength(200);
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasIndex(a => a.BranchId);
            e.HasOne(a => a.Branch).WithMany(b => b.SlugAliases).HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(a => !a.Branch!.IsDeleted);
        });

        // Plans and subscriptions (Models/Billing.cs). Enums stored as text so the rows read
        // plainly in the database and new values never renumber old ones.
        builder.Entity<PriceBook>(e =>
        {
            e.Property(p => p.Module).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Interval).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Currency).HasMaxLength(3);
            e.Property(p => p.PaddlePriceId).HasMaxLength(64);
            e.Property(p => p.ChangedById).HasMaxLength(450);
            e.HasIndex(p => new { p.Module, p.Interval, p.Currency, p.ValidFromUtc }).IsUnique();
        });
        builder.Entity<PlanSettings>(e =>
        {
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Currency).HasMaxLength(3);
            e.Property(p => p.UpdatedById).HasMaxLength(450);
            e.Property(p => p.Version).IsRowVersion();
        });
        builder.Entity<Subscription>(e =>
        {
            e.HasIndex(s => s.OwnerId).IsUnique();
            e.HasOne(s => s.Owner).WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.Interval).HasConversion<string>().HasMaxLength(10);
            e.Property(s => s.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.Currency).HasMaxLength(3);
            e.Property(s => s.ProviderCustomerId).HasMaxLength(100);
            e.Property(s => s.ProviderSubscriptionId).HasMaxLength(100);
            e.Property(s => s.Version).IsRowVersion();
            // Matches the user filter: a removed owner's subscription is never read on its own.
            e.HasQueryFilter(s => !s.Owner!.IsDeleted);
        });
        builder.Entity<SubscriptionItem>(e =>
        {
            e.HasIndex(i => new { i.SubscriptionId, i.Module }).IsUnique();
            e.HasOne(i => i.Subscription).WithMany(s => s.Items).HasForeignKey(i => i.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(i => i.Module).HasConversion<string>().HasMaxLength(20);
            e.HasQueryFilter(i => !i.Subscription!.Owner!.IsDeleted);
        });
        builder.Entity<BillingProfile>(e =>
        {
            e.HasIndex(p => p.OwnerId).IsUnique();
            e.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(p => p.LegalName).HasMaxLength(200);
            e.Property(p => p.Nipt).HasMaxLength(20);
            e.Property(p => p.Address).HasMaxLength(300);
            e.Property(p => p.City).HasMaxLength(100);
            e.Property(p => p.Country).HasMaxLength(2);
            e.Property(p => p.BillingEmail).HasMaxLength(256);
            e.HasQueryFilter(p => !p.Owner!.IsDeleted);
        });
        builder.Entity<SubscriptionAudit>(e =>
        {
            e.HasIndex(a => new { a.SubscriptionId, a.AtUtc });
            e.Property(a => a.Action).HasMaxLength(60);
            e.Property(a => a.ActorId).HasMaxLength(450);
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

        // Bookings. Settings are one row per branch (key = BranchId). Reservations follow
        // their branch's soft delete; the day view reads one branch and date range.
        builder.Entity<ReservationSettings>(e =>
        {
            e.HasKey(x => x.BranchId);
            e.HasOne(x => x.Branch).WithOne().HasForeignKey<ReservationSettings>(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CountryCode).HasMaxLength(4);
            e.Property(x => x.ClosedDates).HasMaxLength(4000);
            e.HasQueryFilter(x => !x.Branch!.IsDeleted);
        });
        builder.Entity<Reservation>(e =>
        {
            e.HasIndex(r => r.PublicId).IsUnique();
            e.HasIndex(r => new { r.BranchId, r.ClientRequestId }).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            e.HasIndex(r => new { r.BranchId, r.StartsAtUtc });
            e.HasIndex(r => new { r.BranchId, r.Phone });
            e.HasIndex(r => new { r.Status, r.StartsAtUtc }); // reminders
            e.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.Name).HasMaxLength(80);
            e.Property(r => r.Phone).HasMaxLength(20);
            e.Property(r => r.Email).HasMaxLength(256);
            e.Property(r => r.Note).HasMaxLength(300);
            e.Property(r => r.StaffNote).HasMaxLength(300);
            e.Property(r => r.Language).HasMaxLength(8);
            e.Property(r => r.Source).HasMaxLength(10);
            e.Property(r => r.CancelledBy).HasMaxLength(12);
            e.Property(r => r.StartsAtLocal).HasColumnType("timestamp without time zone");
            e.HasQueryFilter(r => !r.Branch!.IsDeleted);
        });

        // Websites. One site row per branch; domains are unique across the platform (a host
        // can only ever lead to one restaurant). Both follow the branch's soft delete.
        builder.Entity<BranchSite>(e =>
        {
            e.HasKey(x => x.BranchId);
            e.HasOne(x => x.Branch).WithOne().HasForeignKey<BranchSite>(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Tagline).HasMaxLength(120);
            e.Property(x => x.About).HasMaxLength(3000);
            e.Property(x => x.Instagram).HasMaxLength(60);
            e.Property(x => x.Facebook).HasMaxLength(200);
            e.HasQueryFilter(x => !x.Branch!.IsDeleted);
        });
        builder.Entity<Domain>(e =>
        {
            e.HasIndex(d => d.Host).IsUnique();
            e.HasIndex(d => d.BranchId);
            e.HasOne(d => d.Branch).WithMany().HasForeignKey(d => d.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.Host).HasMaxLength(253);
            e.Property(d => d.Token).HasMaxLength(64);
            e.Property(d => d.LastError).HasMaxLength(300);
            e.Property(d => d.RenderId).HasMaxLength(64);
            e.HasQueryFilter(d => !d.Branch!.IsDeleted);
        });

        // Management: stock, recipes, shifts, daily sales. All follow the branch's soft delete.
        builder.Entity<Ingredient>(e =>
        {
            e.HasIndex(i => new { i.BranchId, i.Name });
            e.HasOne(i => i.Branch).WithMany().HasForeignKey(i => i.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(i => i.Name).HasMaxLength(80);
            e.Property(i => i.Quantity).HasPrecision(18, 3);
            e.Property(i => i.LowLevel).HasPrecision(18, 3);
            e.Property(i => i.CostPerUnit).HasPrecision(18, 2);
            e.HasQueryFilter(i => !i.Branch!.IsDeleted);
        });
        builder.Entity<StockMovement>(e =>
        {
            e.HasIndex(m => new { m.IngredientId, m.CreatedUtc });
            e.HasIndex(m => new { m.BranchId, m.CreatedUtc });
            e.HasIndex(m => m.OrderId);
            e.HasOne(m => m.Ingredient).WithMany().HasForeignKey(m => m.IngredientId).OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.Change).HasPrecision(18, 3);
            e.Property(m => m.QuantityAfter).HasPrecision(18, 3);
            e.Property(m => m.Note).HasMaxLength(200);
            e.Property(m => m.UserName).HasMaxLength(100);
            e.HasQueryFilter(m => !m.Ingredient!.Branch!.IsDeleted);
        });
        builder.Entity<DishIngredient>(e =>
        {
            e.HasIndex(d => new { d.ProductId, d.IngredientId }).IsUnique();
            e.HasIndex(d => d.IngredientId);
            e.HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Ingredient).WithMany().HasForeignKey(d => d.IngredientId).OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.Quantity).HasPrecision(18, 3);
            e.HasQueryFilter(d => !d.Product!.IsDeleted && !d.Ingredient!.Branch!.IsDeleted);
        });
        builder.Entity<Shift>(e =>
        {
            e.HasIndex(s => new { s.BranchId, s.StartsLocal });
            e.HasIndex(s => s.UserId);
            e.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.PersonName).HasMaxLength(80);
            e.Property(s => s.Station).HasMaxLength(40);
            e.Property(s => s.Note).HasMaxLength(200);
            e.Property(s => s.UserId).HasMaxLength(450);
            e.Property(s => s.StartsLocal).HasColumnType("timestamp without time zone");
            e.Property(s => s.EndsLocal).HasColumnType("timestamp without time zone");
            e.HasQueryFilter(s => !s.Branch!.IsDeleted);
        });
        builder.Entity<DailySales>(e =>
        {
            e.HasIndex(d => new { d.BranchId, d.Date }).IsUnique();
            e.HasOne(d => d.Branch).WithMany().HasForeignKey(d => d.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(d => d.Revenue).HasPrecision(18, 2);
            e.Property(d => d.CashTotal).HasPrecision(18, 2);
            e.Property(d => d.CardTotal).HasPrecision(18, 2);
            e.Property(d => d.CloseNote).HasMaxLength(300);
            e.Property(d => d.ClosedByName).HasMaxLength(100);
            e.Property(d => d.TopDishes).HasMaxLength(4000);
            e.HasQueryFilter(d => !d.Branch!.IsDeleted);
        });

        // Guest feedback, read per branch newest first.
        builder.Entity<Feedback>(e =>
        {
            e.HasIndex(f => new { f.BranchId, f.CreatedUtc });
            e.HasOne(f => f.Branch).WithMany().HasForeignKey(f => f.BranchId).OnDelete(DeleteBehavior.Cascade);
            e.Property(f => f.Comment).HasMaxLength(1000);
            e.Property(f => f.Contact).HasMaxLength(120);
            e.Property(f => f.Language).HasMaxLength(8);
            e.HasQueryFilter(f => !f.Branch!.IsDeleted);
        });
        builder.Entity<Branch>().Property(b => b.FeedbackEnabled).HasDefaultValue(true);
        builder.Entity<Branch>().Property(b => b.FeedbackEmailOwner).HasDefaultValue(true);
        builder.Entity<Branch>().Property(b => b.GoogleReviewUrl).HasMaxLength(300);

        // Decimal precision for Price
        builder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
