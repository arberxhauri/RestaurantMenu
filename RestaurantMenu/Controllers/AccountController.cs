using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RestaurantMenu.Models;
using RestaurantMenu.ViewModels;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

[NoIndex]
public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user != null)
                {
                    var result = await _signInManager.PasswordSignInAsync(
                        user.UserName, model.Password, model.RememberMe, false);

                    if (result.Succeeded)
                    {
                        // Check if user must change password
                        if (user.MustChangePassword)
                        {
                            return RedirectToAction("ChangePassword");
                        }

                        var roles = await _userManager.GetRolesAsync(user);
                        if (roles.Contains("ADMIN"))
                        {
                            return RedirectToAction("Index", "Admin");
                        }
                        else
                        {
                            return RedirectToAction("Index", "Dashboard");
                        }
                    }
                }
                ModelState.AddModelError("", "Invalid login attempt");
            }
            return View(model);
        }

        // Invite links from the admin land here. The token is an ASP.NET Identity password-reset
        // token, valid for 3 days (see Program.cs).
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> SetPassword(string? userId, string? token)
        {
            var user = string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(token))
            {
                ViewBag.LinkInvalid = true;
                return View(new SetPasswordViewModel());
            }

            ViewBag.Email = user.Email;
            return View(new SetPasswordViewModel { UserId = userId!, Token = token });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                ViewBag.LinkInvalid = true;
                return View(model);
            }
            ViewBag.Email = user.Email;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == "InvalidToken"))
                {
                    ViewBag.LinkInvalid = true;
                    return View(model);
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(model);
            }

            user.EmailConfirmed = true;
            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = "Your password is set. Welcome to My Quick Menu.";
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                var result = await _userManager.ChangePasswordAsync(
                    user, model.CurrentPassword, model.NewPassword);

                if (result.Succeeded)
                {
                    user.MustChangePassword = false;
                    await _userManager.UpdateAsync(user);
                    await _signInManager.RefreshSignInAsync(user);
                    TempData["Success"] = "Your password was changed.";
                    return await _userManager.IsInRoleAsync(user, "ADMIN")
                        ? RedirectToAction("Index", "Admin")
                        : RedirectToAction("Index", "Dashboard");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(model);
        }
    }