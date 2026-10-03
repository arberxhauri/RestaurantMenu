using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using RestaurantMenu.Filters;
using RestaurantMenu.Models;
using RestaurantMenu.Services;
using RestaurantMenu.ViewModels;

namespace RestaurantMenu.Controllers;

[NoIndex]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly InviteMailer _mailer;
    private readonly SeoService _seo;
    private readonly IMemoryCache _cache;

    /// <summary>At most one reset email per account in this time, however often it's asked for.</summary>
    private static readonly TimeSpan ResetEmailCooldown = TimeSpan.FromMinutes(2);

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        InviteMailer mailer,
        SeoService seo,
        IMemoryCache cache)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _mailer = mailer;
        _seo = seo;
        _cache = cache;
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
                    if (user.MustChangePassword)
                    {
                        return RedirectToAction("ChangePassword");
                    }
                    return await HomeFor(user);
                }
            }
            // Same message whether or not the email has an account.
            ModelState.AddModelError("", "That email and password don't match. Check them, or reset your password.");
        }
        return View(model);
    }

    // ------------------------------------------------------------------ invites

    // Invite links (owner from Admin, staff from Team) land here: a first password, 3 days.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> SetPassword(string? userId, string? token)
    {
        var user = string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(token))
        {
            return LinkProblem(new SetPasswordViewModel(), "invite", "invalid");
        }
        if (user.PasswordHash != null)
        {
            return LinkProblem(new SetPasswordViewModel(), "invite", "used");
        }
        if (!await IsInviteValidAsync(user, token))
        {
            return LinkProblem(new SetPasswordViewModel(), "invite", "invalid");
        }

        ViewBag.Email = user.Email;
        ViewBag.Mode = "invite";
        return View(new SetPasswordViewModel { UserId = userId!, Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
    {
        ViewBag.Mode = "invite";
        var user = string.IsNullOrEmpty(model.UserId) ? null : await _userManager.FindByIdAsync(model.UserId);
        if (user == null)
        {
            return LinkProblem(model, "invite", "invalid");
        }
        if (user.PasswordHash != null)
        {
            return LinkProblem(model, "invite", "used");
        }
        ViewBag.Email = user.Email;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await IsInviteValidAsync(user, model.Token))
        {
            return LinkProblem(model, "invite", "invalid");
        }

        // Checking a token doesn't use it up; adding the password changes the security
        // stamp, which ends this link and every other one sent to the account. Two requests
        // racing each other: the second gets UserAlreadyHasPassword.
        var result = await _userManager.AddPasswordAsync(user, model.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "InvalidToken" or "UserAlreadyHasPassword"))
            {
                return LinkProblem(model, "invite", "used");
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
            return View(model);
        }

        return await FinishAsync(user, "Your password is set. Welcome to My Quick Menu.");
    }

    /// <summary>
    /// Whether this is a valid invite token. Links sent before invites had their own provider
    /// were password-reset tokens from the default provider; they are accepted until they
    /// expire (3 days), so nobody's pending invite breaks.
    /// </summary>
    private async Task<bool> IsInviteValidAsync(ApplicationUser user, string token) =>
        await _userManager.VerifyUserTokenAsync(user, _userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<ApplicationUser>.ConfirmEmailTokenPurpose, token)
        || await _userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);

    // ------------------------------------------------------------------ forgot / reset

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword(bool busy = false)
    {
        ViewBag.CanEmail = _mailer.CanEmail;
        if (busy)
        {
            ModelState.AddModelError("", "Too many reset requests from your network. Wait a few minutes and try again.");
        }
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewBag.CanEmail = _mailer.CanEmail;
        if (!_mailer.CanEmail || !ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        // Removed accounts aren't found (query filter). The answer below is the same either
        // way and the email goes out in the background, so the page never reveals whether
        // an address has an account.
        if (user?.Email != null && _cache.TryGetValue($"pwreset:{user.Id}", out _) == false)
        {
            _cache.Set($"pwreset:{user.Id}", true, ResetEmailCooldown);
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = _seo.Url(Url.Action("ResetPassword", "Account", new { userId = user.Id, token })!);
            _mailer.QueuePasswordReset(user.Email, user.FullName, link);
        }

        TempData["ResetSentTo"] = email;
        return RedirectToAction("ForgotPasswordSent");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordSent()
    {
        if (TempData["ResetSentTo"] is not string email)
        {
            return RedirectToAction("ForgotPassword");
        }
        ViewBag.Email = email;
        return View();
    }

    // Reset links from "Forgot password" land here: 2 hours, once.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(string? userId, string? token)
    {
        var user = string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(token) || !await IsResetTokenValidAsync(user, token))
        {
            return LinkProblem(new SetPasswordViewModel(), "reset", "invalid");
        }

        ViewBag.Email = user.Email;
        ViewBag.Mode = "reset";
        return View("SetPassword", new SetPasswordViewModel { UserId = userId!, Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(SetPasswordViewModel model)
    {
        ViewBag.Mode = "reset";
        var user = string.IsNullOrEmpty(model.UserId) ? null : await _userManager.FindByIdAsync(model.UserId);
        if (user == null)
        {
            return LinkProblem(model, "reset", "invalid");
        }
        ViewBag.Email = user.Email;

        if (!ModelState.IsValid)
        {
            return View("SetPassword", model);
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                return LinkProblem(model, "reset", "invalid");
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
            return View("SetPassword", model);
        }

        // They proved they own the inbox, so any lockout from wrong guesses ends too.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        return await FinishAsync(user, "Your password was changed. You're signed in.");
    }

    private Task<bool> IsResetTokenValidAsync(ApplicationUser user, string token) =>
        _userManager.VerifyUserTokenAsync(user, _userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);

    // ------------------------------------------------------------------ shared

    /// <param name="mode">invite or reset.</param>
    /// <param name="state">invalid (expired, used or mangled) or used (the invite's password is already set).</param>
    private ViewResult LinkProblem(SetPasswordViewModel model, string mode, string state)
    {
        ViewBag.Mode = mode;
        ViewBag.LinkInvalid = true;
        ViewBag.LinkState = state;
        return View("SetPassword", model);
    }

    /// <summary>After a password is set from an emailed link: the inbox is proven, sign in, go home.</summary>
    private async Task<IActionResult> FinishAsync(ApplicationUser user, string message)
    {
        user.EmailConfirmed = true;
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = message;
        return await HomeFor(user);
    }

    private async Task<IActionResult> HomeFor(ApplicationUser user) =>
        await _userManager.IsInRoleAsync(user, "ADMIN")
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Dashboard");

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
                return await HomeFor(user);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }
        return View(model);
    }
}
