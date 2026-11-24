using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Helpers;

/// <summary>One page in the back office's side panel.</summary>
public sealed record NavItem(string Key, string Label, string Icon, string Href);

/// <summary>A section of the side panel (Reports, Stations, Menu…).</summary>
public sealed record NavGroup(string Key, string Label, string Icon, IReadOnlyList<NavItem> Items);

/// <summary>Who is looking, at which branch (null: none yet), and what that branch's plan includes.</summary>
public sealed record NavContext(int? BranchId, BranchRole Role, bool IsOwner, bool IsAdmin, IReadOnlySet<BillingModule> Modules);

/// <summary>
/// The back office's side panel in one place (docs/PLAN-backoffice.md §2): its sections and pages, the
/// address of each page for the current branch, who sees what (the same rules as BranchAccess), which
/// pages the active plan includes, and which page is current. Pages that don't exist yet aren't listed.
/// </summary>
public static class BackofficeNav
{
    private enum Audience
    {
        /// <summary>Needs a branch and a role on it that allows <see cref="Page.Permission"/>.</summary>
        Branch,
        /// <summary>Anyone who works on a branch or owns an account.</summary>
        Workspace,
        /// <summary>Account owners (billing).</summary>
        Owner,
        /// <summary>Everyone signed in.</summary>
        Everyone,
        /// <summary>Platform admins.</summary>
        Admin
    }

    private sealed record Section(string Key, string Label, string Icon);

    private sealed record Page(string Key, string Section, string Label, string Icon, Func<int, string> Href,
        Audience Audience, BranchPermission Permission = BranchPermission.View, BillingModule? Module = null);

    private static readonly Section[] Sections =
    {
        new("dashboard", "Dashboard", "ph-squares-four"),
        new("reports", "Reports", "ph-chart-bar"),
        new("stations", "Stations", "ph-cooking-pot"),
        new("tables", "Tables", "ph-armchair"),
        new("menu", "Menu", "ph-book-open-text"),
        new("stock", "Stock", "ph-package"),
        new("config", "Configuration", "ph-gear-six"),
        new("admin", "Admin", "ph-shield-check")
    };

    private static readonly Page[] Pages =
    {
        new("dashboard", "dashboard", "Overview", "ph-squares-four", _ => "/manage", Audience.Workspace),

        new("reports.cash", "reports", "Cash report", "ph-coins", b => $"/pos/{b}/cash", Audience.Branch, BranchPermission.ViewInsights, BillingModule.Ordering),
        new("reports.sales", "reports", "Sales", "ph-currency-eur", b => $"/branch/{b}/sales", Audience.Branch, BranchPermission.ViewInsights, BillingModule.Management),
        new("reports.insights", "reports", "Menu analytics", "ph-chart-line-up", b => $"/Branch/Insights/{b}", Audience.Branch, BranchPermission.ViewInsights),
        new("reports.feedback", "reports", "Guest feedback", "ph-chat-circle-text", b => $"/branch/{b}/feedback", Audience.Branch, BranchPermission.ViewInsights),

        new("stations.pos", "stations", "POS", "ph-cash-register", b => $"/pos/{b}", Audience.Branch, BranchPermission.Kitchen, BillingModule.Ordering),
        new("stations.kitchen", "stations", "Kitchen", "ph-chef-hat", b => $"/kitchen/{b}", Audience.Branch, BranchPermission.Kitchen, BillingModule.Ordering),
        new("stations.bar", "stations", "Bar", "ph-wine", b => $"/kitchen/{b}/bar", Audience.Branch, BranchPermission.Kitchen, BillingModule.Ordering),

        new("tables.floor", "tables", "Live floor", "ph-armchair", b => $"/branch/{b}/tables/floor", Audience.Branch, BranchPermission.EditBranch, BillingModule.Ordering),
        new("tables.tables", "tables", "Tables & QR codes", "ph-qr-code", b => $"/branch/{b}/tables", Audience.Branch, BranchPermission.EditBranch, BillingModule.Ordering),
        new("tables.bookings", "tables", "Reservations", "ph-calendar-check", b => $"/branch/{b}/bookings", Audience.Branch, BranchPermission.Bookings, BillingModule.Bookings),

        new("menu.items", "menu", "Items & categories", "ph-fork-knife", b => $"/Branch/Details/{b}", Audience.Branch),
        new("menu.recipes", "menu", "Recipes", "ph-list-checks", b => $"/branch/{b}/stock/recipes", Audience.Branch, BranchPermission.EditBranch, BillingModule.Management),
        new("menu.discounts", "menu", "Discounts & happy hours", "ph-tag", b => $"/branch/{b}/discounts", Audience.Branch, BranchPermission.EditBranch, BillingModule.Ordering),

        new("stock.items", "stock", "Stock items", "ph-package", b => $"/branch/{b}/stock", Audience.Branch, BranchPermission.Stock, BillingModule.Management),
        new("stock.waste", "stock", "Waste", "ph-trash", b => $"/branch/{b}/stock/waste", Audience.Branch, BranchPermission.Stock, BillingModule.Management),

        new("config.branch", "config", "Branch settings", "ph-storefront", b => $"/Branch/Edit/{b}", Audience.Branch, BranchPermission.EditBranch),
        new("config.team", "config", "Users & roles", "ph-users-three", b => $"/Branch/Details/{b}#team", Audience.Branch, BranchPermission.ManageTeam),
        new("config.shifts", "config", "Shifts", "ph-clock", b => $"/branch/{b}/shifts", Audience.Branch, BranchPermission.Stock, BillingModule.Management),
        new("config.website", "config", "Website & domains", "ph-globe", b => $"/branch/{b}/website", Audience.Branch, BranchPermission.EditBranch, BillingModule.Website),
        new("config.branches", "config", "Branches", "ph-buildings", _ => "/Dashboard", Audience.Workspace),
        new("config.billing", "config", "Billing", "ph-receipt", _ => "/billing", Audience.Owner),
        new("config.password", "config", "Password", "ph-key", _ => "/Account/ChangePassword", Audience.Everyone),

        new("admin.users", "admin", "Users", "ph-users-three", _ => "/Admin", Audience.Admin),
        new("admin.prices", "admin", "Plans & prices", "ph-currency-eur", _ => "/Admin/Prices", Audience.Admin),
        new("admin.invoices", "admin", "Invoices", "ph-receipt", _ => "/Admin/Billing", Audience.Admin),
        new("admin.referrals", "admin", "AI referrals", "ph-sparkle", _ => "/Admin/Referrals", Audience.Admin)
    };

    /// <summary>The side panel for this person and branch: only sections with something to show.</summary>
    public static IReadOnlyList<NavGroup> Build(NavContext context) =>
        Sections
            .Select(s => new NavGroup(s.Key, s.Label, s.Icon,
                Pages.Where(p => p.Section == s.Key && Visible(p, context))
                    .Select(p => new NavItem(p.Key, p.Label, p.Icon, p.Href(context.BranchId ?? 0)))
                    .ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();

    private static bool Visible(Page page, NavContext c)
    {
        // Features outside the current plan are intentionally absent from navigation and the
        // command palette. RequireModuleAttribute still protects direct URLs server-side.
        if (page.Module is { } module && !c.Modules.Contains(module)) return false;

        return page.Audience switch
        {
            Audience.Branch => c.BranchId != null && BranchAccess.Allows(c.Role, page.Permission),
            Audience.Workspace => c.IsOwner || c.Role > BranchRole.None,
            Audience.Owner => c.IsOwner,
            Audience.Everyone => true,
            Audience.Admin => c.IsAdmin,
            _ => false
        };
    }

    /// <summary>The address of a page for a branch, or null for anything that isn't a known page (no open redirects).</summary>
    public static string? HrefFor(string? key, int branchId) =>
        Pages.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Href(branchId);

    /// <summary>Which side-panel item the current page belongs to, from its controller and action.</summary>
    public static string? ActiveKey(string? controller, string? action)
    {
        static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return controller?.ToLowerInvariant() switch
        {
            "manage" => "dashboard",
            "sales" => "reports.sales",
            "feedback" => "reports.feedback",
            "kitchen" => Is(action, "Bar") ? "stations.bar" : "stations.kitchen",
            "pos" => Is(action, "Cash") ? "reports.cash" : "stations.pos",
            "tables" => Is(action, "Floor") ? "tables.floor" : "tables.tables",
            "bookings" => "tables.bookings",
            "category" or "product" => "menu.items",
            "stock" => Is(action, "Recipes") ? "menu.recipes" : Is(action, "Waste") ? "stock.waste" : "stock.items",
            "discounts" => "menu.discounts",
            "shifts" => "config.shifts",
            "website" => "config.website",
            "dashboard" => "config.branches",
            "billing" => "config.billing",
            "account" => "config.password",
            "branch" when Is(action, "Insights") => "reports.insights",
            "branch" when Is(action, "Edit") => "config.branch",
            "branch" when Is(action, "Create") => "config.branches",
            "branch" => "menu.items",
            "admin" when Is(action, "Prices") => "admin.prices",
            "admin" when Is(action, "Billing") => "admin.invoices",
            "admin" when Is(action, "Referrals") => "admin.referrals",
            "admin" => "admin.users",
            _ => null
        };
    }
}
