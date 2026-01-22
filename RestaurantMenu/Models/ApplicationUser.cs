using Microsoft.AspNetCore.Identity;

namespace RestaurantMenu.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; }
    public string NIPT { get; set; }
    public int NumberOfBranches { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
    public bool MustChangePassword { get; set; } = false;
        
    public ICollection<Branch> Branches { get; set; }
}