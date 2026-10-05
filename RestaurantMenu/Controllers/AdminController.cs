using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Services;
using RestaurantMenu.Models;
using RestaurantMenu.ViewModels;

using RestaurantMenu.Filters;
namespace RestaurantMenu.Controllers;

[Authorize(Roles = "ADMIN")]
    [NoIndex]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly InviteMailer _mailer;
        private readonly EmailService _email;
        private readonly SeoService _seo;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            InviteMailer mailer,
            EmailService email,
            SeoService seo)
        {
            _userManager = userManager;
            _context = context;
            _mailer = mailer;
            _email = email;
            _seo = seo;
        }

        public async Task<IActionResult> Index()
        {
            // Owners only: admins manage the platform and must not appear (or be deletable) here.
            var ownerIds = (await _userManager.GetUsersInRoleAsync("OWNER")).Select(u => u.Id).ToList();
            var users = await _userManager.Users
                .Where(u => ownerIds.Contains(u.Id))
                .Include(u => u.Branches)
                .OrderBy(u => u.FullName)
                .ToListAsync();
            ViewBag.Email = _email;
            // Restaurants' own domains: without a Render API key, each has to be added in Render by hand.
            ViewBag.Domains = await _context.Domains.AsNoTracking().Include(d => d.Branch)
                .OrderBy(d => d.Verified).ThenByDescending(d => d.CreatedUtc).Take(200).ToListAsync();
            return View(users);
        }

        [HttpGet]
        public IActionResult CreateUser()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            model.Email = model.Email?.Trim() ?? "";
            if (ModelState.IsValid)
            {
                // Including removed accounts (hidden by the query filter, so Identity's own check
                // misses them and the insert would hit the unique index): their email still
                // belongs to them, as in TeamController.Invite.
                var normalized = _userManager.NormalizeEmail(model.Email);
                var existing = await _context.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.NormalizedUserName == normalized);
                if (existing != null)
                {
                    ModelState.AddModelError(nameof(model.Email), existing.IsDeleted
                        ? $"This email belonged to an account that was removed{(existing.DeletedOnUtc is { } at ? $" on {at:d MMM yyyy}" : "")}. Removed accounts keep their email, so use another address."
                        : "There's already an account with this email. To get them in, use the new invite link on the owners list, or a password reset link.");
                    return View(model);
                }
            }
            if (ModelState.IsValid)
            {
                // No password is created here. The owner sets their own through an invite link,
                // so a password is never shown on screen or sent in plain text.
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    NIPT = model.NIPT,
                    NumberOfBranches = model.NumberOfBranches,
                    MustChangePassword = false,
                    EmailConfirmed = false
                };

                IdentityResult result;
                try
                {
                    result = await _userManager.CreateAsync(user);
                }
                catch (DbUpdateException)
                {
                    // Someone took the email between the check above and the insert.
                    ModelState.AddModelError(nameof(model.Email), "There's already an account with this email.");
                    return View(model);
                }

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "OWNER");
                    await DeliverInvite(user);
                    return RedirectToAction("Index");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(model);
        }

        /// <summary>New invite link for an owner who hasn't set a password yet (or lost the email).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendInvite(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || !await _userManager.IsInRoleAsync(user, "OWNER"))
            {
                return NotFound();
            }

            await DeliverInvite(user);
            return RedirectToAction("Index");
        }

        private async Task DeliverInvite(ApplicationUser user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = _seo.Url(Url.Action("SetPassword", "Account", new { userId = user.Id, token })!);

            if (await _mailer.SendSetPasswordAsync(user.Email!, user.FullName, "Your My Quick Menu account",
                    "Your My Quick Menu account is ready. Choose your password to sign in and set up your menus.", link))
            {
                TempData["Success"] = $"Invite sent to {user.Email}. The link works for {AccountTokens.InviteLifespanText}.";
                return;
            }
            if (_mailer.CanEmail)
            {
                TempData["Warning"] = "The invite email could not be sent. Copy the link below and send it yourself.";
            }

            // No email configured (or it failed): show the link once so the admin can send it.
            TempData["InviteLink"] = link;
            TempData["InviteFor"] = $"{user.FullName} ({user.Email})";
        }

        /// <summary>
        /// A password reset link for any owner or staff account, for when someone is locked
        /// out and can't use "Forgot password" (email not set up, or the email never arrives).
        /// Emailed to them when possible; otherwise shown once to pass on.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetLink(string? email)
        {
            var user = string.IsNullOrWhiteSpace(email) ? null : await _userManager.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                TempData["Error"] = $"No account uses {email?.Trim()}.";
                return RedirectToAction("Index");
            }
            // Admins reset their own through "Forgot password"; one admin can't take over another.
            if (await _userManager.IsInRoleAsync(user, "ADMIN"))
            {
                TempData["Error"] = "Admin passwords can only be reset by their owner, with \"Forgot password\" on the sign-in page.";
                return RedirectToAction("Index");
            }
            // Never set a password: send an invite instead.
            if (user.PasswordHash == null)
            {
                await DeliverInvite(user);
                return RedirectToAction("Index");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = _seo.Url(Url.Action("ResetPassword", "Account", new { userId = user.Id, token })!);
            if (await _mailer.SendNoticeAsync(user.Email!, user.FullName, "Reset your My Quick Menu password",
                    $"Your My Quick Menu administrator sent you a link to choose a new password. It works once, for {AccountTokens.ResetLifespanText}. Until you use it, your current password keeps working.",
                    "Choose a new password", link))
            {
                TempData["Success"] = $"Reset link sent to {user.Email}. It works for {AccountTokens.ResetLifespanText}.";
                return RedirectToAction("Index");
            }
            if (_mailer.CanEmail)
            {
                TempData["Warning"] = "The email could not be sent. Copy the link below and send it yourself.";
            }
            TempData["InviteLink"] = link;
            TempData["InviteFor"] = $"{user.FullName} ({user.Email})";
            TempData["InviteKind"] = "reset";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SoftDeleteUser(string userId)
        {
            var currentUserId = _userManager.GetUserId(User);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return RedirectToAction("Index");
            }

            // Never remove yourself or another admin from here: that can lock everyone out.
            if (user.Id == currentUserId || await _userManager.IsInRoleAsync(user, "ADMIN"))
            {
                TempData["Error"] = "Admin accounts can't be removed from this page.";
                return RedirectToAction("Index");
            }

            user.IsDeleted = true;
            user.DeletedOnUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            // Signs them out on their next request.
            await _userManager.UpdateSecurityStampAsync(user);

            // Their menus go offline with them (soft delete, so they can be restored).
            var branches = await _context.Branches.Where(b => b.UserId == user.Id).ToListAsync();
            _context.Branches.RemoveRange(branches);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"{user.FullName} was removed and their {branches.Count} branch menu(s) are offline.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBranchLimit(string userId, int numberOfBranches)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && numberOfBranches is >= 1 and <= 100)
            {
                user.NumberOfBranches = numberOfBranches;
                await _userManager.UpdateAsync(user);
                TempData["Success"] = $"{user.FullName} can now have {numberOfBranches} branch(es).";
            }
            return RedirectToAction("Index");
        }
    }
