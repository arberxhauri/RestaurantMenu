using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
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
        private readonly SubscriptionService _subscriptions;
        private readonly IEntitlementService _entitlements;
        private readonly BillingOptions _billing;
        private readonly SiteHosts _hosts;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            InviteMailer mailer,
            EmailService email,
            SeoService seo,
            SubscriptionService subscriptions,
            IEntitlementService entitlements,
            IOptions<BillingOptions> billing,
            SiteHosts hosts)
        {
            _subscriptions = subscriptions;
            _entitlements = entitlements;
            _billing = billing.Value;
            _hosts = hosts;
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
            var plans = new Dictionary<string, Entitlements>();
            foreach (var u in users) plans[u.Id] = await _entitlements.ForOwnerAsync(u.Id);
            ViewBag.Plans = plans;
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
                    // Legacy terms, as every owner had before plans; change them on the Plan page.
                    _subscriptions.AddLegacy(user, DateTime.UtcNow);
                    await _context.SaveChangesAsync();
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

        // ---------------------------------------------------------------- plans

        /// <summary>An owner's plan: modules, branches, trial or paid period, and its history.</summary>
        [HttpGet]
        public async Task<IActionResult> Plan(string id)
        {
            var page = await PlanPageAsync(id, null, null);
            return page == null ? NotFound() : View(page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Plan(string id, PlanForm form)
        {
            var owner = await OwnerAsync(id);
            if (owner == null) return NotFound();

            var errors = new List<string>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (form.Branches is < 1 or > 100) errors.Add("Branches must be between 1 and 100.");
            if (form.SeatsPerBranch is < 0 or > 100) errors.Add("Team members per branch must be between 0 and 100 (leave empty for the default).");
            if (form.Kind == PlanKind.Trial && form.TrialEnds is { } t && t < today) errors.Add("The trial's last day can't be in the past. Choose Read-only to end it now.");
            if (form.Kind == PlanKind.Active && form.PeriodEnds is { } p && p < today) errors.Add("The paid period's last day can't be in the past.");
            if (form.Kind != PlanKind.Legacy && form.Modules.Contains(BillingModule.OwnDomain) && form.OwnDomains is < 1 or > 100)
                errors.Add("Own domains must be between 1 and 100.");
            if (errors.Count > 0)
            {
                return View(await PlanPageAsync(id, form, errors));
            }

            var change = new SubscriptionChange(form.Kind, form.Modules.ToHashSet(), form.Branches, form.SeatsPerBranch,
                form.Modules.Contains(BillingModule.OwnDomain) ? form.OwnDomains : 0, form.Interval,
                PlanForm.EndOf(form.TrialEnds), PlanForm.EndOf(form.PeriodEnds));
            var problem = await _subscriptions.ApplyAsync(id, change, form.Version, _userManager.GetUserId(User), DateTime.UtcNow);
            if (problem != null)
            {
                TempData["Error"] = problem;
                return RedirectToAction(nameof(Plan), new { id });
            }
            _hosts.Invalidate();

            var live = await _context.Branches.CountAsync(b => b.UserId == id);
            TempData[live > form.Branches ? "Warning" : "Success"] = live > form.Branches
                ? $"Plan saved. {owner.FullName} has {live} branches but the plan includes {form.Branches}: the newest are paused until they choose which stay active."
                : $"Plan saved for {owner.FullName}.";
            return RedirectToAction(nameof(Plan), new { id });
        }

        /// <summary>Adds days to a trial (from its current end, or from today if it already ended).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtendTrial(string id, int days)
        {
            var sub = await _subscriptions.FindAsync(id);
            if (sub == null || sub.IsLegacy || days is < 1 or > 365) return NotFound();
            var now = DateTime.UtcNow;
            var from = sub.Status == SubscriptionStatus.Trialing && sub.TrialEndsUtc > now ? sub.TrialEndsUtc.Value : now.Date.AddDays(1);
            var change = new SubscriptionChange(PlanKind.Trial, sub.Items.Select(i => i.Module).ToHashSet(), sub.BranchQuantity, sub.SeatsPerBranch,
                sub.Items.FirstOrDefault(i => i.Module == BillingModule.OwnDomain)?.Quantity ?? 0, sub.Interval, from.AddDays(days), null);
            var problem = await _subscriptions.ApplyAsync(id, change, sub.Version, _userManager.GetUserId(User), now);
            if (problem == null) _hosts.Invalidate();
            TempData[problem == null ? "Success" : "Error"] = problem ?? $"Trial extended to {from.AddDays(days).AddDays(-1):d MMM yyyy}.";
            return RedirectToAction(nameof(Plan), new { id });
        }

        private async Task<ApplicationUser?> OwnerAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            return user != null && await _userManager.IsInRoleAsync(user, "OWNER") ? user : null;
        }

        private async Task<PlanPage?> PlanPageAsync(string id, PlanForm? form, List<string>? errors)
        {
            var owner = await OwnerAsync(id);
            if (owner == null) return null;
            var sub = await _context.Subscriptions.AsNoTracking().Include(s => s.Items).FirstOrDefaultAsync(s => s.OwnerId == id);
            var now = await _entitlements.ForOwnerAsync(id);
            var live = await _context.Branches.CountAsync(b => b.UserId == id);
            var domains = await _context.Domains.CountAsync(d => d.Branch!.UserId == id);
            var prices = new Dictionary<(BillingModule, BillingInterval), int>();
            foreach (var interval in Enum.GetValues<BillingInterval>())
                foreach (var (module, cents) in await _subscriptions.CurrentPricesAsync(_billing.Currency, interval, DateTime.UtcNow))
                    prices[(module, interval)] = cents;
            var history = sub == null ? new List<SubscriptionAudit>()
                : await _context.SubscriptionAudits.AsNoTracking().Where(a => a.SubscriptionId == sub.Id)
                    .OrderByDescending(a => a.AtUtc).Take(30).ToListAsync();
            var actorIds = history.Where(a => a.ActorId != null).Select(a => a.ActorId!).Distinct().ToList();
            var actors = await _context.Users.IgnoreQueryFilters().Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.FullName);
            return new PlanPage(owner, sub, now, live, domains, _billing.SeatsPerBranch,
                form ?? PlanForm.From(sub, now, live), prices, _billing.Currency, history, actors, errors);
        }
    }
