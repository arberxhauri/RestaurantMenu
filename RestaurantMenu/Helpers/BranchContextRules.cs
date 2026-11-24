namespace RestaurantMenu.Helpers;

/// <summary>
/// Which branch the back office is showing (side panel links, branch switcher). The page's own branch
/// when it has one, else the one remembered from the last branch page, else the first branch.
/// </summary>
public static class BranchContextRules
{
    /// <summary>Controllers whose {id} is always a branch id (branch/{id}/…, kitchen/{id}).</summary>
    private static readonly HashSet<string> BranchControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tables", "Kitchen", "Stock", "Shifts", "Sales", "Bookings", "Feedback", "Website", "Discounts", "Pos", "Reports"
    };

    /// <summary>BranchController actions whose {id} is the branch (Create has none; the rest are POSTs).</summary>
    private static readonly HashSet<string> BranchPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Details", "Edit", "Insights", "Qr"
    };

    /// <param name="fromRoute">The branch the page names, if any (<see cref="BranchInRoute"/>).</param>
    /// <param name="remembered">The branch in the back-office cookie, if any.</param>
    /// <param name="mine">The branches the person may open, in display order.</param>
    public static int? Resolve(int? fromRoute, int? remembered, IReadOnlyList<int> mine)
    {
        if (fromRoute is { } r && mine.Contains(r)) return r;
        if (remembered is { } c && mine.Contains(c)) return c;
        return mine.Count > 0 ? mine[0] : null;
    }

    /// <summary>The branch a page belongs to, from its route: null when the page isn't about one branch.</summary>
    /// <param name="branchIdQuery">?branchId= (the "new category" and "new dish" forms).</param>
    public static int? BranchInRoute(string? controller, string? action, int? id, int? branchIdQuery)
    {
        if (controller == null) return null;
        if (BranchControllers.Contains(controller)) return id;
        if (string.Equals(controller, "Branch", StringComparison.OrdinalIgnoreCase))
            return action != null && BranchPages.Contains(action) ? id : null;
        if ((string.Equals(controller, "Category", StringComparison.OrdinalIgnoreCase)
             || string.Equals(controller, "Product", StringComparison.OrdinalIgnoreCase))
            && string.Equals(action, "Create", StringComparison.OrdinalIgnoreCase))
            return branchIdQuery;
        return null;
    }
}
