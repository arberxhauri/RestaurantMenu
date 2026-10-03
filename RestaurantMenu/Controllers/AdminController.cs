using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            InviteMailer mailer)
        {
            _userManager = userManager;
            _context = context;
            _mailer = mailer;
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

                var result = await _userManager.CreateAsync(user);

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
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = Url.Action("SetPassword", "Account", new { userId = user.Id, token }, Request.Scheme)!;

            if (await _mailer.SendSetPasswordAsync(user.Email!, user.FullName, "Your My Quick Menu account",
                    "Your My Quick Menu account is ready. Choose your password to sign in:", link))
            {
                TempData["Success"] = $"Invite sent to {user.Email}. The link works for 3 days.";
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
