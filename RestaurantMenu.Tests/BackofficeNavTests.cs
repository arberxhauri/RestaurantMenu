using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// The back office's side panel: which sections and pages someone sees for the branch they're on,
/// where each link goes, which ones the plan includes, and which page is "current".
/// </summary>
public class BackofficeNavTests
{
    private static readonly HashSet<BillingModule> AllModules = Enum.GetValues<BillingModule>().ToHashSet();

    private static NavContext Owner(int? branch = 5, IReadOnlySet<BillingModule>? modules = null) =>
        new(branch, BranchRole.Owner, IsOwner: true, IsAdmin: false, modules ?? AllModules);

    private static IEnumerable<string> Keys(IReadOnlyList<NavGroup> groups) =>
        groups.SelectMany(g => g.Items).Select(i => i.Key);

    private static NavItem Item(IReadOnlyList<NavGroup> groups, string key) =>
        groups.SelectMany(g => g.Items).Single(i => i.Key == key);

    [Fact]
    public void An_owner_sees_the_sections_in_the_planned_order() =>
        Assert.Equal(new[] { "dashboard", "reports", "stations", "tables", "menu", "stock", "config" },
            BackofficeNav.Build(Owner()).Select(g => g.Key));

    [Theory]
    [InlineData("dashboard", "/manage")]
    [InlineData("reports.sales", "/branch/5/sales")]
    [InlineData("reports.insights", "/Branch/Insights/5")]
    [InlineData("reports.feedback", "/branch/5/feedback")]
    [InlineData("stations.kitchen", "/kitchen/5")]
    [InlineData("tables.tables", "/branch/5/tables")]
    [InlineData("tables.bookings", "/branch/5/bookings")]
    [InlineData("menu.items", "/Branch/Details/5")]
    [InlineData("menu.recipes", "/branch/5/stock/recipes")]
    [InlineData("stock.items", "/branch/5/stock")]
    [InlineData("config.branch", "/Branch/Edit/5")]
    [InlineData("config.team", "/Branch/Details/5#team")]
    [InlineData("config.shifts", "/branch/5/shifts")]
    [InlineData("config.website", "/branch/5/website")]
    [InlineData("config.branches", "/Dashboard")]
    [InlineData("config.billing", "/billing")]
    [InlineData("config.password", "/Account/ChangePassword")]
    public void Each_page_links_to_its_existing_address_for_the_current_branch(string key, string href) =>
        Assert.Equal(href, Item(BackofficeNav.Build(Owner()), key).Href);

    [Fact]
    public void A_waiter_sees_only_what_their_role_opens()
    {
        var keys = Keys(BackofficeNav.Build(new NavContext(5, BranchRole.Editor, IsOwner: false, IsAdmin: false, AllModules))).ToList();

        Assert.Equal(new[] { "dashboard", "stations.kitchen", "tables.bookings", "menu.items", "stock.items", "stock.waste", "config.shifts", "config.branches", "config.password" }, keys);
    }

    [Fact]
    public void A_manager_sees_reports_and_settings_but_not_the_team_or_billing()
    {
        var keys = Keys(BackofficeNav.Build(new NavContext(5, BranchRole.Manager, IsOwner: false, IsAdmin: false, AllModules))).ToList();

        Assert.Contains("reports.sales", keys);
        Assert.Contains("config.branch", keys);
        Assert.Contains("menu.recipes", keys);
        Assert.DoesNotContain("config.team", keys);
        Assert.DoesNotContain("config.billing", keys);
    }

    [Theory]
    [InlineData("stations.kitchen", BillingModule.Ordering)]
    [InlineData("tables.tables", BillingModule.Ordering)]
    [InlineData("tables.bookings", BillingModule.Bookings)]
    [InlineData("stock.items", BillingModule.Management)]
    [InlineData("reports.sales", BillingModule.Management)]
    [InlineData("config.website", BillingModule.Website)]
    public void A_page_outside_the_plan_is_hidden(string key, BillingModule missing)
    {
        var modules = AllModules.Where(m => m != missing).ToHashSet();

        var groups = BackofficeNav.Build(Owner(modules: modules));

        Assert.DoesNotContain(key, Keys(groups));
        Assert.Contains("menu.items", Keys(groups));
    }

    [Fact]
    public void Without_a_branch_only_the_account_pages_are_shown() =>
        Assert.Equal(new[] { "dashboard", "config.branches", "config.billing", "config.password" },
            Keys(BackofficeNav.Build(Owner(branch: null))));

    [Fact]
    public void Platform_admins_get_their_own_section_last()
    {
        var groups = BackofficeNav.Build(new NavContext(null, BranchRole.None, IsOwner: false, IsAdmin: true, AllModules));

        Assert.Equal("admin", groups[^1].Key);
        Assert.Equal(new[] { "admin.users", "admin.prices", "admin.invoices", "admin.referrals" }, groups[^1].Items.Select(i => i.Key));
    }

    [Fact]
    public void Owners_get_no_admin_section() =>
        Assert.DoesNotContain(BackofficeNav.Build(Owner()), g => g.Key == "admin");

    [Theory]
    [InlineData("Manage", "Index", "dashboard")]
    [InlineData("Sales", "Index", "reports.sales")]
    [InlineData("Branch", "Insights", "reports.insights")]
    [InlineData("Feedback", "Index", "reports.feedback")]
    [InlineData("Kitchen", "Index", "stations.kitchen")]
    [InlineData("Tables", "Index", "tables.tables")]
    [InlineData("Bookings", "Settings", "tables.bookings")]
    [InlineData("Branch", "Details", "menu.items")]
    [InlineData("Category", "Edit", "menu.items")]
    [InlineData("Product", "Create", "menu.items")]
    [InlineData("Stock", "Recipes", "menu.recipes")]
    [InlineData("Stock", "History", "stock.items")]
    [InlineData("Branch", "Edit", "config.branch")]
    [InlineData("Branch", "Create", "config.branches")]
    [InlineData("Dashboard", "Index", "config.branches")]
    [InlineData("Shifts", "Index", "config.shifts")]
    [InlineData("Website", "Index", "config.website")]
    [InlineData("Billing", "Index", "config.billing")]
    [InlineData("Account", "ChangePassword", "config.password")]
    [InlineData("Admin", "Index", "admin.users")]
    [InlineData("Admin", "Prices", "admin.prices")]
    [InlineData("Admin", "Billing", "admin.invoices")]
    [InlineData("Admin", "Referrals", "admin.referrals")]
    public void The_current_page_marks_its_item(string controller, string action, string key) =>
        Assert.Equal(key, BackofficeNav.ActiveKey(controller, action));

    [Fact]
    public void An_unknown_page_marks_nothing() => Assert.Null(BackofficeNav.ActiveKey("Signup", "Index"));

    [Theory]
    [InlineData("stock.items", 7, "/branch/7/stock")]
    [InlineData("config.billing", 7, "/billing")]
    [InlineData("dashboard", 7, "/manage")]
    public void Switching_branch_keeps_the_section(string key, int branch, string href) =>
        Assert.Equal(href, BackofficeNav.HrefFor(key, branch));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example")]
    [InlineData("menu.items/../../x")]
    public void Switching_branch_only_follows_known_sections(string? key) =>
        Assert.Null(BackofficeNav.HrefFor(key, 7));
}

/// <summary>Which branch the back office is showing: the page's own, the remembered one, or the first.</summary>
public class BranchContextRulesTests
{
    private static readonly int[] Mine = { 3, 5, 8 };

    [Fact]
    public void The_branch_in_the_address_wins() => Assert.Equal(8, BranchContextRules.Resolve(8, 5, Mine));

    [Fact]
    public void A_branch_that_isnt_yours_in_the_address_is_ignored() => Assert.Equal(5, BranchContextRules.Resolve(99, 5, Mine));

    [Fact]
    public void The_remembered_branch_is_used_on_pages_without_one() => Assert.Equal(5, BranchContextRules.Resolve(null, 5, Mine));

    [Fact]
    public void A_remembered_branch_you_lost_falls_back_to_the_first() => Assert.Equal(3, BranchContextRules.Resolve(null, 42, Mine));

    [Fact]
    public void No_branches_means_no_branch() => Assert.Null(BranchContextRules.Resolve(4, 4, Array.Empty<int>()));

    [Theory]
    [InlineData("Tables", "Index", 4, null, 4)]
    [InlineData("Kitchen", "Index", 4, null, 4)]
    [InlineData("Stock", "Recipes", 4, null, 4)]
    [InlineData("Branch", "Details", 4, null, 4)]
    [InlineData("Branch", "Create", null, null, null)]
    [InlineData("Category", "Edit", 9, null, null)]
    [InlineData("Category", "Create", null, 3, 3)]
    [InlineData("Product", "Create", null, 3, 3)]
    [InlineData("Billing", "Index", null, null, null)]
    public void Only_branch_pages_name_a_branch(string controller, string action, int? id, int? branchIdQuery, int? expected) =>
        Assert.Equal(expected, BranchContextRules.BranchInRoute(controller, action, id, branchIdQuery));
}
