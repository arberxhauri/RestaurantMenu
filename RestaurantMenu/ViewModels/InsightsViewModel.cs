using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.ViewModels;

public class InsightsViewModel
{
    public required Branch Branch { get; init; }
    public required MenuInsights.BranchReport Report { get; init; }
}

/// <summary>One stat tile: a value, and its change against the previous period of equal length.</summary>
/// <param name="Period">How the comparison is described, e.g. "previous 7 days".</param>
public record KpiModel(string Label, int Value, int Previous, string Period);
