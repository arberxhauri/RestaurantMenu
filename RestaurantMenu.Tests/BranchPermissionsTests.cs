using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// Who may do what on a branch (docs/PLAN-backoffice.md §5, phase 8). The roles are not a ladder any
/// more: a cashier, a cook and a stock keeper each see their own part of the back office.
/// </summary>
public class BranchPermissionsTests
{
    private static readonly BranchPermission[] Everything = Enum.GetValues<BranchPermission>();

    private static BranchPermission[] AllowedFor(BranchRole role) =>
        Everything.Where(p => BranchPermissions.Allows(role, p)).ToArray();

    [Fact]
    public void The_owner_may_do_everything() =>
        Assert.Equal(Everything, AllowedFor(BranchRole.Owner));

    [Fact]
    public void Someone_without_a_role_may_do_nothing() =>
        Assert.Empty(AllowedFor(BranchRole.None));

    [Fact]
    public void A_manager_may_do_everything_but_the_team_and_deleting_the_branch()
    {
        var expected = Everything.Except(new[] { BranchPermission.ManageTeam, BranchPermission.DeleteBranch });
        Assert.Equal(expected, AllowedFor(BranchRole.Manager));
    }

    [Fact]
    public void A_waiter_keeps_everything_an_editor_had_and_gains_the_floor_plan() =>
        Assert.Equal(new[]
        {
            BranchPermission.View, BranchPermission.ViewMenu, BranchPermission.EditDishes, BranchPermission.Pos,
            BranchPermission.Kitchen, BranchPermission.Bar, BranchPermission.FloorPlan, BranchPermission.Bookings,
            BranchPermission.Stock, BranchPermission.ViewShifts
        }, AllowedFor(BranchRole.Waiter));

    [Fact]
    public void A_cashier_takes_orders_and_payments_and_sees_the_cash_report() =>
        Assert.Equal(new[]
        {
            BranchPermission.View, BranchPermission.ViewMenu, BranchPermission.Pos, BranchPermission.CashReport,
            BranchPermission.ViewShifts
        }, AllowedFor(BranchRole.Cashier));

    [Fact]
    public void Kitchen_staff_see_only_the_kitchen_screen() =>
        Assert.Equal(new[] { BranchPermission.View, BranchPermission.Kitchen, BranchPermission.ViewShifts },
            AllowedFor(BranchRole.Kitchen));

    [Fact]
    public void Bar_staff_see_only_the_bar_screen() =>
        Assert.Equal(new[] { BranchPermission.View, BranchPermission.Bar, BranchPermission.ViewShifts },
            AllowedFor(BranchRole.Bar));

    [Fact]
    public void Stock_staff_see_stock_and_the_menu_but_cannot_change_dishes() =>
        Assert.Equal(new[] { BranchPermission.View, BranchPermission.ViewMenu, BranchPermission.Stock, BranchPermission.ViewShifts },
            AllowedFor(BranchRole.Stock));

    [Theory]
    [InlineData(BranchRole.Kitchen, BranchPermission.Bar)]
    [InlineData(BranchRole.Bar, BranchPermission.Kitchen)]
    [InlineData(BranchRole.Cashier, BranchPermission.EditDishes)]
    [InlineData(BranchRole.Waiter, BranchPermission.CashReport)]
    [InlineData(BranchRole.Stock, BranchPermission.Pos)]
    [InlineData(BranchRole.Kitchen, BranchPermission.ViewMenu)]
    public void A_role_does_not_reach_the_other_roles_screens(BranchRole role, BranchPermission permission) =>
        Assert.False(BranchPermissions.Allows(role, permission));

    [Theory]
    [InlineData(BranchMemberRole.Waiter, BranchRole.Waiter)]
    [InlineData(BranchMemberRole.Manager, BranchRole.Manager)]
    [InlineData(BranchMemberRole.Cashier, BranchRole.Cashier)]
    [InlineData(BranchMemberRole.Kitchen, BranchRole.Kitchen)]
    [InlineData(BranchMemberRole.Bar, BranchRole.Bar)]
    [InlineData(BranchMemberRole.Stock, BranchRole.Stock)]
    public void A_team_role_becomes_the_same_branch_role(BranchMemberRole member, BranchRole role) =>
        Assert.Equal(role, BranchPermissions.RoleOf(member));

    [Fact]
    public void No_membership_or_an_unknown_stored_value_is_no_role()
    {
        Assert.Equal(BranchRole.None, BranchPermissions.RoleOf(null));
        Assert.Equal(BranchRole.None, BranchPermissions.RoleOf((BranchMemberRole)99));
    }

    [Fact]
    public void Stored_role_numbers_never_change()
    {
        // Persisted as int: existing editors (1) and managers (2) keep their access.
        Assert.Equal(1, (int)BranchMemberRole.Waiter);
        Assert.Equal(2, (int)BranchMemberRole.Manager);
        Assert.Equal(3, (int)BranchMemberRole.Cashier);
        Assert.Equal(4, (int)BranchMemberRole.Kitchen);
        Assert.Equal(5, (int)BranchMemberRole.Bar);
        Assert.Equal(6, (int)BranchMemberRole.Stock);
    }

    [Fact]
    public void The_database_filter_matches_the_rule_for_every_role_and_permission()
    {
        // Branches(permission) filters members with MemberRolesWith; it must agree with Allows.
        foreach (var permission in Everything)
        foreach (var member in Enum.GetValues<BranchMemberRole>())
        {
            Assert.Equal(BranchPermissions.Allows(BranchPermissions.RoleOf(member), permission),
                BranchPermissions.MemberRolesWith(permission).Contains(member));
        }
    }

    [Fact]
    public void The_team_form_offers_every_role_with_a_label_and_description()
    {
        Assert.Equal(Enum.GetValues<BranchMemberRole>().OrderBy(r => r), BranchPermissions.MemberRoles.OrderBy(r => r));
        Assert.All(BranchPermissions.MemberRoles, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(BranchPermissions.Label(r)));
            Assert.False(string.IsNullOrWhiteSpace(BranchPermissions.Describe(r)));
        });
        Assert.Equal(BranchMemberRole.Waiter, BranchPermissions.MemberRoles[0]);
    }

    [Theory]
    [InlineData(BranchRole.Owner, "Owner")]
    [InlineData(BranchRole.Waiter, "Waiter")]
    [InlineData(BranchRole.Cashier, "Cashier")]
    [InlineData(BranchRole.Kitchen, "Kitchen")]
    [InlineData(BranchRole.Bar, "Bar")]
    [InlineData(BranchRole.Stock, "Stock")]
    [InlineData(BranchRole.Manager, "Manager")]
    public void Each_role_has_a_name_people_recognise(BranchRole role, string label) =>
        Assert.Equal(label, BranchPermissions.Label(role));

    [Theory]
    [InlineData(BranchRole.Kitchen, "/kitchen/7")]
    [InlineData(BranchRole.Bar, "/kitchen/7/bar")]
    [InlineData(BranchRole.Cashier, "/pos/7")]
    [InlineData(BranchRole.Stock, "/branch/7/stock")]
    [InlineData(BranchRole.Waiter, "/Branch/Details/7")]
    [InlineData(BranchRole.Manager, "/Branch/Details/7")]
    [InlineData(BranchRole.Owner, "/Branch/Details/7")]
    public void Each_role_lands_on_its_own_screen(BranchRole role, string href) =>
        Assert.Equal(href, BranchPermissions.Home(role, 7));
}
