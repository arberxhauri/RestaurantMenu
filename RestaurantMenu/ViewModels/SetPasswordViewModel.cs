using System.ComponentModel.DataAnnotations;

namespace RestaurantMenu.ViewModels;

public class SetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "The two passwords don't match.")]
    [Display(Name = "Repeat password")]
    public string ConfirmPassword { get; set; } = "";
}
