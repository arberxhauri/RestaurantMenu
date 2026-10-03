using RestaurantMenu.Models;

namespace RestaurantMenu.ViewModels;

/// <summary>One person on a branch's team, for the Team section on Branch Details.</summary>
/// <param name="Pending">Invited but hasn't set a password yet.</param>
public record TeamMemberRow(int Id, string Name, string Email, BranchMemberRole Role, bool Pending);
