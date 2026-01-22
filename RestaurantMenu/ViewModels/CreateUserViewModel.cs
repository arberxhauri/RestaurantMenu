using System.ComponentModel.DataAnnotations;

namespace RestaurantMenu.ViewModels;

public class CreateUserViewModel
{
    [Required]
    [Display(Name = "Full Name")]
    public string FullName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    public string NIPT { get; set; }

    [Required]
    [Display(Name = "Number of Branches")]
    [Range(1, 100)]
    public int NumberOfBranches { get; set; }
}