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
        private readonly PlanSettingsService _plan;
        private readonly SiteHosts _hosts;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            InviteMailer mailer,
            EmailService email,
            SeoService seo,
            SubscriptionService subscriptions,
            IEntitlementService entitlements,
            PlanSettingsService plan,
            SiteHosts hosts)
        {
            _subscriptions = subscriptions;
            _entitlements = entitlements;
            _plan = plan;
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
                    await _subscriptions.AddLegacyAsync(user, DateTime.UtcNow);
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

        /// <summary>Lets a self-serve signup in: the account opens, the trial starts, they get an email.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string userId)
        {
            var signup = HttpContext.RequestServices.GetRequiredService<SignupService>();
            var ok = await signup.ApproveAsync(userId, _userManager.GetUserId(User), DateTime.UtcNow);
            var user = ok ? await _userManager.FindByIdAsync(userId) : null;
            TempData[ok ? "Success" : "Error"] = ok
                ? $"{user!.FullName} is in: their trial has started and they've been emailed."
                : "That account isn't waiting for approval any more.";
            return RedirectToAction("Index");
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

        // ---------------------------------------------------------------- billing

        /// <summary>Invoices across the platform: mark bank transfers paid, void, resend, and run the billing checks.</summary>
        [HttpGet]
        public async Task<IActionResult> Billing(string show = "open")
        {
            var now = DateTime.UtcNow;
            var q = _context.Invoices.AsNoTracking().Include(i => i.Owner).AsQueryable();
            q = show switch
            {
                "overdue" => q.Where(i => i.Status == InvoiceStatus.Open && i.DueUtc < now),
                "paid" => q.Where(i => i.Status == InvoiceStatus.Paid),
                "void" => q.Where(i => i.Status == InvoiceStatus.Void),
                "all" => q,
                _ => q.Where(i => i.Status == InvoiceStatus.Open)
            };
            ViewBag.Show = show;
            ViewBag.OpenTotal = await _context.Invoices.Where(i => i.Status == InvoiceStatus.Open).SumAsync(i => (int?)i.TotalCents) ?? 0;
            ViewBag.Overdue = await _context.Invoices.CountAsync(i => i.Status == InvoiceStatus.Open && i.DueUtc < now);
            ViewBag.Settings = await _plan.GetAsync();
            ViewBag.FailedEvents = await _context.BillingEvents.AsNoTracking().Where(e => e.ProcessedUtc == null && e.Attempts > 0).ToListAsync();
            return View(await q.OrderByDescending(i => i.IssuedUtc).Take(200).ToListAsync());
        }

        /// <summary>A bank transfer arrived: recorded in the billing inbox, then applied straight away.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkPaid(int id, DateOnly? paidOn, string? note)
        {
            var billing = HttpContext.RequestServices.GetRequiredService<BillingService>();
            var runner = HttpContext.RequestServices.GetRequiredService<BillingRunner>();
            var paidUtc = paidOn is { } d && d <= DateOnly.FromDateTime(DateTime.UtcNow) ? d.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc) : DateTime.UtcNow;
            var note2 = string.IsNullOrWhiteSpace(note) ? null : note.Trim().Length > 300 ? note.Trim()[..300] : note.Trim();
            if (!await billing.MarkPaidAsync(id, paidUtc, note2, _userManager.GetUserId(User)))
            {
                TempData["Error"] = "That invoice isn't open any more.";
                return RedirectToAction(nameof(Billing));
            }
            await runner.ProcessEventsAsync(DateTime.UtcNow);
            var inv = await _context.Invoices.AsNoTracking().Include(i => i.Owner).FirstAsync(i => i.Id == id);
            TempData[inv.Status == InvoiceStatus.Paid ? "Success" : "Warning"] = inv.Status == InvoiceStatus.Paid
                ? $"{inv.Number} is paid. {inv.Owner!.FullName}'s plan is active until {BillingText.LastDay(inv.PeriodEndUtc, "en")} and they've been thanked."
                : $"{inv.Number}: the payment is recorded but couldn't be applied yet; it's retried automatically (see Problems below).";
            return RedirectToAction(nameof(Billing));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidInvoice(int id)
        {
            var billing = HttpContext.RequestServices.GetRequiredService<BillingService>();
            var ok = await billing.VoidAsync(id, _userManager.GetUserId(User), DateTime.UtcNow);
            TempData[ok ? "Success" : "Error"] = ok ? "Invoice voided. It stays in the list; its number is never reused." : "Only open invoices can be voided.";
            return RedirectToAction(nameof(Billing));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendInvoice(int id)
        {
            var mailer = HttpContext.RequestServices.GetRequiredService<BillingMailer>();
            var inv = await _context.Invoices.Include(i => i.Lines).Include(i => i.Owner).FirstOrDefaultAsync(i => i.Id == id);
            if (inv == null) return NotFound();
            var sent = await mailer.SendInvoiceAsync(inv.Owner!, inv, force: true);
            TempData[sent ? "Success" : "Error"] = sent ? $"{inv.Number} sent again to {inv.BuyerEmail}." : "The email couldn't be sent. Is email set up?";
            return RedirectToAction(nameof(Billing));
        }

        [HttpGet]
        public async Task<IActionResult> InvoicePdf(int id)
        {
            var inv = await _context.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id);
            return inv == null ? NotFound() : File(Services.InvoicePdf.Render(inv), "application/pdf", Services.InvoicePdf.FileName(inv));
        }

        /// <summary>Runs what the billing worker does every minute, now: payments, status changes, renewals, emails.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunBilling()
        {
            var runner = HttpContext.RequestServices.GetRequiredService<BillingRunner>();
            var result = await runner.RunAsync(DateTime.UtcNow);
            TempData[result.Problems.Count == 0 ? "Success" : "Warning"] = $"Billing checks done: {result}.";
            return RedirectToAction(nameof(Billing));
        }

        // ---------------------------------------------------------------- plans & prices

        /// <summary>
        /// The platform's plan settings and price book, all in the database: currency, trial
        /// length, staff per branch, grace days, the signup switches, and a price per module per
        /// month and year. A changed price is a new dated row; the old one stays as history.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Prices()
        {
            var settings = await _plan.GetAsync();
            return View(await PricesPageAsync(PlanSettingsForm.From(settings, await _plan.PriceTableAsync(DateTime.UtcNow)), null));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Prices(PlanSettingsForm form)
        {
            var errors = new List<string>();
            form.Currency = (form.Currency ?? "").Trim().ToUpperInvariant();
            if (form.Currency.Length != 3 || !form.Currency.All(char.IsAsciiLetterUpper)) errors.Add("The currency is a 3-letter code, e.g. EUR or ALL.");
            if (form.TrialDays is < 1 or > 365) errors.Add("The trial is between 1 and 365 days.");
            if (form.SeatsPerBranch is < 0 or > 100) errors.Add("Staff per branch is between 0 and 100.");
            if (form.GraceDays is < 0 or > 90) errors.Add("Grace days are between 0 and 90.");
            if (!decimal.TryParse((form.VatPercent ?? "0").Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var vat) || vat is < 0 or > 50)
                errors.Add("VAT is a percentage between 0 and 50, e.g. 20 (or 0 while you're not VAT-registered).");
            if (form.InvoiceDueDays is < 1 or > 90) errors.Add("Days to pay an invoice are between 1 and 90.");
            if (form.RenewalLeadDays is < 0 or > 60) errors.Add("Renewal invoices go out 0 to 60 days before a period ends.");
            var iban = (form.OperatorIban ?? "").Replace(" ", "").ToUpperInvariant();
            if (iban.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(iban, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{8,30}$"))
                errors.Add("The IBAN doesn't look right: two letters, two digits, then up to 30 letters and digits (e.g. AL47 2121 1009 0000 0002 3569 8741).");

            var wanted = new Dictionary<(BillingModule, BillingInterval), int?>();
            foreach (var m in EntitlementRules.AllModules.Where(m => m != BillingModule.Sms))
            foreach (var i in Enum.GetValues<BillingInterval>())
            {
                var raw = form.Prices.GetValueOrDefault(PlanSettingsForm.Key(m, i));
                if (MoneyInput.TryParseCents(raw, out var cents)) wanted[(m, i)] = cents;
                else errors.Add($"{EntitlementRules.Name(m)} ({(i == BillingInterval.Year ? "yearly" : "monthly")}): \"{raw}\" isn't an amount. Write it like 15 or 15.50.");
            }
            if (errors.Count > 0) return View(await PricesPageAsync(form, errors));

            var actor = _userManager.GetUserId(User);
            var now = DateTime.UtcNow;
            var problem = await _plan.SaveAsync(new PlanSettingsInput(form.Currency, form.TrialDays, form.SeatsPerBranch, form.GraceDays,
                form.SignupEnabled, form.SignupRequireApproval,
                new InvoiceSettingsInput(form.OperatorName, form.OperatorNipt, form.OperatorAddress, form.OperatorEmail, form.OperatorIban,
                    form.OperatorBank, form.OperatorSwift, vat, form.InvoiceDueDays, form.RenewalLeadDays, form.CardPaymentsEnabled)), form.Version, actor, now);
            if (problem != null)
            {
                TempData["Error"] = problem;
                return RedirectToAction(nameof(Prices));
            }
            // Prices go in the (possibly new) currency.
            var changed = await _plan.SetPricesAsync(wanted, actor, now);
            var signupNote = form.SignupEnabled && !_email.IsConfigured ? " Signup is switched on but stays closed until email is set up." : "";
            // New prices go to Paddle at once when card payments are on.
            // Warnings (signupNote) colour the message; plain information doesn't.
            var paddle = HttpContext.RequestServices.GetRequiredService<PaddleBilling>();
            var info = "";
            if (form.CardPaymentsEnabled && !paddle.IsConfigured) signupNote += " Card payments stay off until the Billing__Paddle__… settings are set on the server.";
            else if (form.CardPaymentsEnabled)
            {
                try { var (made, gone) = await paddle.SyncPricesAsync(now); if (made + gone > 0) info = $" Paddle: {made} price(s) created, {gone} archived."; }
                catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException) { signupNote += $" Paddle price sync failed ({ex.Message}); try Sync again."; }
            }
            TempData[signupNote.Length > 0 ? "Warning" : "Success"] =
                $"Saved. {(changed == 0 ? "No prices changed." : $"{changed} price{(changed == 1 ? "" : "s")} changed; existing subscriptions keep theirs.")}{info}{signupNote}";
            return RedirectToAction(nameof(Prices));
        }

        /// <summary>Mirrors the current prices into Paddle (products and prices), archiving replaced ones.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncPaddle()
        {
            var paddle = HttpContext.RequestServices.GetRequiredService<PaddleBilling>();
            if (!paddle.IsConfigured)
            {
                TempData["Error"] = "Paddle isn't set up: set Billing__Paddle__ApiKey, __WebhookSecret and __ClientToken on the server.";
                return RedirectToAction(nameof(Prices));
            }
            try
            {
                var (made, gone) = await paddle.SyncPricesAsync(DateTime.UtcNow);
                TempData["Success"] = $"Prices are in Paddle: {made} created, {gone} archived.";
            }
            catch (Exception ex) when (ex is PaddleException or HttpRequestException or TaskCanceledException)
            {
                TempData["Error"] = $"Paddle refused the sync: {ex.Message}";
            }
            return RedirectToAction(nameof(Prices));
        }

        private async Task<PricesPage> PricesPageAsync(PlanSettingsForm form, List<string>? errors)
        {
            var settings = await _plan.GetAsync();
            var history = await _plan.HistoryAsync(40);
            var ids = history.Select(h => h.ChangedById).Append(settings.UpdatedById).Where(id => id != null).Select(id => id!).Distinct().ToList();
            var actors = await _context.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? u.FullName);
            var paddleOptions = HttpContext.RequestServices.GetRequiredService<IOptions<PaddleOptions>>().Value;
            ViewBag.Paddle = paddleOptions.IsConfigured ? (paddleOptions.IsSandbox ? "sandbox" : "production") : null;
            ViewBag.PaddleSynced = await _context.PriceBook.CountAsync(p => p.PaddlePriceId != null && p.PaddleArchivedUtc == null);
            return new PricesPage(form, await _plan.PriceTableAsync(DateTime.UtcNow), history, actors, settings.UpdatedUtc,
                settings.UpdatedById == null ? null : actors.GetValueOrDefault(settings.UpdatedById), _email.IsConfigured, _email.Problem, errors);
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
            var prices = await _plan.PriceTableAsync(DateTime.UtcNow);
            var settings = await _plan.GetAsync();
            var history = sub == null ? new List<SubscriptionAudit>()
                : await _context.SubscriptionAudits.AsNoTracking().Where(a => a.SubscriptionId == sub.Id)
                    .OrderByDescending(a => a.AtUtc).Take(30).ToListAsync();
            var actorIds = history.Where(a => a.ActorId != null).Select(a => a.ActorId!).Distinct().ToList();
            var actors = await _context.Users.IgnoreQueryFilters().Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.FullName);
            return new PlanPage(owner, sub, now, live, domains, settings.SeatsPerBranch,
                form ?? PlanForm.From(sub, now, live), prices, settings.Currency, history, actors, errors);
        }
    }
