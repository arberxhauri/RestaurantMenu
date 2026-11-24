using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// What the current user (or station device) is to a branch. Not a ladder: compare with
/// <see cref="BranchPermissions.Allows"/>, never with &lt; or &gt;. Not persisted.
/// </summary>
public enum BranchRole
{
    None = 0,
    Waiter = 1,
    Manager = 2,
    Owner = 3,
    Cashier = 4,
    Kitchen = 5,
    Bar = 6,
    Stock = 7
}

/// <summary>Everything the back office lets someone do to a branch. Who has each: <see cref="BranchPermissions"/>.</summary>
public enum BranchPermission
{
    /// <summary>The branch is on their list (everyone with a role).</summary>
    View,
    /// <summary>The branch page with its menu, read-only unless they may edit.</summary>
    ViewMenu,
    /// <summary>Add and edit dishes, sold out, recommend, reorder dishes, photos, translate.</summary>
    EditDishes,
    /// <summary>The POS: take orders, discounts, request bills, take payment, print bills.</summary>
    Pos,
    /// <summary>The kitchen screen: move tickets along, pause new orders.</summary>
    Kitchen,
    /// <summary>The bar screen: move tickets along, pause new orders.</summary>
    Bar,
    /// <summary>The day's payments (end-of-day cash count).</summary>
    CashReport,
    /// <summary>The live floor plan, and seating bookings there (with <see cref="Bookings"/>).</summary>
    FloorPlan,
    /// <summary>The bookings day view: add, confirm, seat, cancel, mark no-shows, close a day.</summary>
    Bookings,
    /// <summary>Stock levels: record deliveries, usage, waste and counts.</summary>
    Stock,
    /// <summary>See the shift rota.</summary>
    ViewShifts,
    /// <summary>Delete and restore dishes.</summary>
    DeleteDishes,
    /// <summary>Create, edit, delete and reorder categories.</summary>
    EditCategories,
    /// <summary>Branch details, hours, brand, tables, booking settings, website, ingredients and recipes, discounts, shifts.</summary>
    EditBranch,
    /// <summary>Insights, sales, reports and the end-of-day close.</summary>
    ViewInsights,
    /// <summary>QR codes and printing.</summary>
    Print,
    /// <summary>Set up, re-pair and remove shared kitchen and bar tablets (station devices).</summary>
    ManageDevices,
    /// <summary>Invite, change and remove staff (owner only).</summary>
    ManageTeam,
    /// <summary>Delete or restore the branch itself (owner only).</summary>
    DeleteBranch
}

/// <summary>
/// The single permission table (docs/PLAN-backoffice.md §5). The owner may do everything; every
/// other role may do exactly what is listed for it. BranchAccess, the side panel, the views and
/// the kitchen hub all ask here, so the rule can't drift.
/// </summary>
public static class BranchPermissions
{
    private static readonly BranchPermission[] Everyone = { BranchPermission.View, BranchPermission.ViewShifts };

    private static readonly IReadOnlyDictionary<BranchRole, HashSet<BranchPermission>> Table =
        new Dictionary<BranchRole, HashSet<BranchPermission>>
        {
            // Editors before phase 8: nothing they could do is taken away.
            [BranchRole.Waiter] = With(BranchPermission.ViewMenu, BranchPermission.EditDishes, BranchPermission.Pos,
                BranchPermission.Kitchen, BranchPermission.Bar, BranchPermission.FloorPlan, BranchPermission.Bookings,
                BranchPermission.Stock),
            [BranchRole.Cashier] = With(BranchPermission.ViewMenu, BranchPermission.Pos, BranchPermission.CashReport),
            [BranchRole.Kitchen] = With(BranchPermission.Kitchen),
            [BranchRole.Bar] = With(BranchPermission.Bar),
            [BranchRole.Stock] = With(BranchPermission.ViewMenu, BranchPermission.Stock),
            [BranchRole.Manager] = Enum.GetValues<BranchPermission>()
                .Where(p => p is not (BranchPermission.ManageTeam or BranchPermission.DeleteBranch)).ToHashSet()
        };

    private static HashSet<BranchPermission> With(params BranchPermission[] permissions) =>
        Everyone.Concat(permissions).ToHashSet();

    /// <summary>The roles an owner can give, in the order the team form lists them.</summary>
    public static IReadOnlyList<BranchMemberRole> MemberRoles { get; } = new[]
    {
        BranchMemberRole.Waiter, BranchMemberRole.Cashier, BranchMemberRole.Kitchen,
        BranchMemberRole.Bar, BranchMemberRole.Stock, BranchMemberRole.Manager
    };

    public static bool Allows(BranchRole role, BranchPermission permission) =>
        role == BranchRole.Owner || (Table.TryGetValue(role, out var allowed) && allowed.Contains(permission));

    /// <summary>The stored team roles that have a permission (for "member may" in a single database query).</summary>
    public static IReadOnlyList<BranchMemberRole> MemberRolesWith(BranchPermission permission) =>
        MemberRoles.Where(m => Allows(RoleOf(m), permission)).ToList();

    /// <summary>A team member's role on the branch; None without a membership or for a value this version doesn't know.</summary>
    public static BranchRole RoleOf(BranchMemberRole? member) => member switch
    {
        BranchMemberRole.Waiter => BranchRole.Waiter,
        BranchMemberRole.Manager => BranchRole.Manager,
        BranchMemberRole.Cashier => BranchRole.Cashier,
        BranchMemberRole.Kitchen => BranchRole.Kitchen,
        BranchMemberRole.Bar => BranchRole.Bar,
        BranchMemberRole.Stock => BranchRole.Stock,
        _ => BranchRole.None
    };

    public static string Label(BranchRole role) => role switch
    {
        BranchRole.None => "No access",
        _ => role.ToString()
    };

    public static string Label(BranchMemberRole role) => Label(RoleOf(role));

    /// <summary>One line for the team form: what someone with this role can open.</summary>
    public static string Describe(BranchMemberRole role) => role switch
    {
        BranchMemberRole.Waiter => "POS, kitchen and bar screens, floor plan, bookings, dishes and stock.",
        BranchMemberRole.Cashier => "POS, payments, bills and the day's cash report.",
        BranchMemberRole.Kitchen => "The kitchen screen only.",
        BranchMemberRole.Bar => "The bar screen only.",
        BranchMemberRole.Stock => "Stock items, deliveries, counts and waste.",
        BranchMemberRole.Manager => "Everything in the branch except the team, billing and deleting it.",
        _ => ""
    };

    /// <summary>Where someone lands when they open a branch: their own screen, or the branch page.</summary>
    public static string Home(BranchRole role, int branchId) => role switch
    {
        BranchRole.Kitchen => $"/kitchen/{branchId}",
        BranchRole.Bar => $"/kitchen/{branchId}/bar",
        BranchRole.Cashier => $"/pos/{branchId}",
        BranchRole.Stock => $"/branch/{branchId}/stock",
        _ => $"/Branch/Details/{branchId}"
    };
}
