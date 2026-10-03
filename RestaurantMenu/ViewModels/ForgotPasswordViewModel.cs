using System.ComponentModel.DataAnnotations;

namespace RestaurantMenu.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter the email you sign in with.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(256)]
    public string Email { get; set; } = "";
}
