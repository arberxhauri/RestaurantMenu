using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using RestaurantMenu.Filters;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;
using RestaurantMenu.Services;
using RestaurantMenu.ViewModels;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The self-serve funnel, in English and Albanian (?lang=sq), all of it working without
/// JavaScript: /pricing (pick modules and branches) → /signup (account, restaurant, terms) →
/// /signup/check (the confirmation email) → /signup/confirm (the link) → the dashboard, or a
/// "we're reviewing it" page when the admin approves new accounts. Everything 404s while
/// signup is closed (SignupService.IsOpen). Only /pricing is for search engines.
/// </summary>
public class SignupController : Controller
{
    private static readonly TimeSpan MinFillTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxFormAge = TimeSpan.FromHours(3);
    /// <summary>At most one confirmation email per address in this time, however often it's asked for.</summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    private readonly SignupService _signup;
    private readonly SubscriptionService _subscriptions;
    private readonly PlanSettingsService _plan;
    private readonly BranchSlugs _slugs;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SeoService _seo;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _formTime;

    public SignupController(SignupService signup, SubscriptionService subscriptions, PlanSettingsService plan, BranchSlugs slugs, SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users, SeoService seo, IMemoryCache cache, IDataProtectionProvider protection)
    {
        _signup = signup;
        _subscriptions = subscriptions;
        _plan = plan;
        _slugs = slugs;
        _signIn = signIn;
        _users = users;
        _seo = seo;
        _cache = cache;
        _formTime = protection.CreateProtector("Signup.FormTime");
    }

    private string Lang(string? requested) => SignupText.Pick(requested, Request.Headers.AcceptLanguage);

    // ---------------------------------------------------------------- pricing

    [HttpGet("pricing")]
    public async Task<IActionResult> Pricing(List<string>? m, string? b, string? i, string? preset, string? lang)
    {
        if (!await _signup.IsOpenAsync()) return NotFound();
        var language = Lang(lang);
        var w = SignupText.For(language);
        var prices = await _plan.PriceTableAsync(DateTime.UtcNow);
        var currency = (await _plan.GetAsync()).Currency;
        var selection = PricingRules.Parse(m, b, i, preset);
        var presets = PricingRules.Presets
            .Select(p =>
            {
                var s = PricingRules.Parse(null, "1", "Month", p.Key);
                return (p.Key, s, PricingRules.Quote(s, prices, currency));
            }).ToList();

        ViewData["Seo"] = Seo(w.PricingTitle, string.Format(w.PricingIntro, await _signup.TrialDaysAsync()), language, "/pricing");
        ViewData["SignInLabel"] = w.SignIn;
        return View(new PricingPage(w, language, selection, PricingRules.Quote(selection, prices, currency), presets, prices, currency, await _signup.TrialDaysAsync()));
    }

    // ---------------------------------------------------------------- signup

    [HttpGet("signup")]
    [NoIndex]
    public async Task<IActionResult> Index(List<string>? m, string? b, string? i, string? lang, bool busy = false)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        var language = Lang(lang);
        if (!await _signup.IsOpenAsync()) return StatusPage(new SignupStatusPage(SignupText.For(language), language, "closed"));
        var form = new SignupForm { Lang = language, Country = language == "sq" ? "AL" : null, M = m, B = b, I = i };
        return await FormAsync(form, new Dictionary<string, string>(), null, busy);
    }

    [HttpPost("signup")]
    [NoIndex]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> Index(SignupForm form)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        var language = Lang(form.Lang);
        form.Lang = language;
        var w = SignupText.For(language);
        if (!await _signup.IsOpenAsync()) return StatusPage(new SignupStatusPage(w, language, "closed"));

        var name = SignupRules.Clean(form.FullName);
        var email = (form.Email ?? "").Trim();
        var restaurant = SignupRules.Clean(form.Restaurant);

        // Bots: a filled honeypot or a form sent faster than a person can type gets the
        // ordinary "check your email" page, and nothing is created or sent.
        var shownAt = ShownAt(form.T);
        if (!string.IsNullOrEmpty(form.Website) || shownAt is { } t0 && DateTime.UtcNow - t0 < MinFillTime)
        {
            return RedirectToAction(nameof(Check), new { e = email, lang = language });
        }

        var errors = new Dictionary<string, string>();
        if (!SignupRules.IsName(name)) errors[nameof(SignupForm.FullName)] = w.ErrName;
        if (!SignupRules.IsEmail(email)) errors[nameof(SignupForm.Email)] = w.ErrEmail;
        if (!SignupRules.IsPassword(form.Password)) errors[nameof(SignupForm.Password)] = w.ErrPassword;
        if (!SignupRules.IsRestaurant(restaurant)) errors[nameof(SignupForm.Restaurant)] = w.ErrRestaurant;
        if (!SignupText.IsCountry(form.Country)) errors[nameof(SignupForm.Country)] = w.ErrCountry;
        if (!form.Terms) errors[nameof(SignupForm.Terms)] = w.ErrTerms;
        // A form left open for hours (or tampered with) is just shown again with a fresh clock.
        if (shownAt == null || DateTime.UtcNow - shownAt > MaxFormAge) errors.TryAdd("", w.ErrExpired);
        if (errors.Count > 0)
        {
            form.Password = null;
            return await FormAsync(form, errors, null, false);
        }

        var plan = PricingRules.Parse(form.M, form.B, form.I);
        var (outcome, user, problems) = await _signup.CreateAsync(
            new SignupInput(name, email, form.Password!, restaurant, form.Country!, language), plan, DateTime.UtcNow);
        if (outcome != SignupOutcome.Created || user == null)
        {
            form.Password = null;
            // Identity's own refusals (rare: its rules match ours) are shown as the password rule.
            var problem = outcome switch
            {
                SignupOutcome.EmailRemoved => w.ErrRemoved,
                _ when problems.Any() => w.ErrPassword,
                _ => w.ErrExists
            };
            return await FormAsync(form, new Dictionary<string, string>(), problem, false);
        }

        var sent = await _signup.SendConfirmationAsync(user);
        _cache.Set(CooldownKey(user.Email!), true, ResendCooldown);
        return RedirectToAction(nameof(Check), new { e = user.Email, lang = language, failed = sent ? (bool?)null : true });
    }

    /// <summary>The live "your link will be …" hint (signup.js). The form works without it.</summary>
    [HttpGet("signup/link")]
    [NoIndex]
    [EnableRateLimiting("signup-check")]
    public async Task<IActionResult> Link(string? name)
    {
        if (!await _signup.IsOpenAsync()) return NotFound();
        var clean = SignupRules.Clean(name);
        if (!SignupRules.IsRestaurant(clean)) return Ok(new { ok = false });
        var (slug, taken) = await _slugs.SuggestAsync(clean);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { ok = true, slug, taken, url = $"{MenuBase()}{slug}" });
    }

    // ---------------------------------------------------------------- confirmation

    [HttpGet("signup/check")]
    [NoIndex]
    public async Task<IActionResult> Check(string? e, string? lang, bool sent = false, bool failed = false, bool busy = false)
    {
        if (!await _signup.IsOpenAsync()) return NotFound();
        var language = Lang(lang);
        return StatusPage(new SignupStatusPage(SignupText.For(language), language, "check", Email: e, Sent: sent, Failed: failed, Busy: busy));
    }

    /// <summary>
    /// "Send the link again": the same answer whatever the address, so it can't tell anyone
    /// which emails have accounts. One email per address per 2 minutes, and the "signup" limit per client.
    /// </summary>
    [HttpPost("signup/resend")]
    [NoIndex]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> Resend(string? email, string? lang)
    {
        if (!await _signup.IsOpenAsync()) return NotFound();
        var language = Lang(lang);
        email = (email ?? "").Trim();
        var failed = false;
        if (SignupRules.IsEmail(email) && !_cache.TryGetValue(CooldownKey(email), out _))
        {
            _cache.Set(CooldownKey(email), true, ResendCooldown);
            var user = await _users.FindByEmailAsync(email);
            if (user is { AwaitingEmail: true }) failed = !await _signup.SendConfirmationAsync(user);
        }
        return RedirectToAction(nameof(Check), new { e = email, lang = language, sent = !failed, failed = failed ? (bool?)true : null });
    }

    /// <summary>
    /// The emailed link lands on a page with a button, and only the button (POST) confirms:
    /// mail scanners that open links can't confirm an account or sign anyone in.
    /// </summary>
    [HttpGet("signup/confirm")]
    [NoIndex]
    public async Task<IActionResult> Confirm(string? u, string? t, string? lang)
    {
        var language = Lang(lang);
        var w = SignupText.For(language);
        var user = string.IsNullOrEmpty(u) || string.IsNullOrEmpty(t) ? null : await _users.FindByIdAsync(u);
        if (user == null || user.SignupSource != SignupSource.SelfServe) return StatusPage(new SignupStatusPage(w, language, "invalid"));
        if (user.EmailConfirmed)
        {
            if (user.AwaitingApproval) return StatusPage(new SignupStatusPage(w, language, "pending"));
            TempData["Success"] = w.VerifyAlready;
            return RedirectToAction("Login", "Account");
        }
        return StatusPage(new SignupStatusPage(w, language, "confirm", Email: user.Email, UserId: u, Token: t));
    }

    [HttpPost("signup/confirm")]
    [NoIndex]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPost(string? u, string? t, string? lang)
    {
        var language = Lang(lang);
        var w = SignupText.For(language);
        var (outcome, user) = await _signup.ConfirmAsync(u, t, DateTime.UtcNow);
        switch (outcome)
        {
            case ConfirmOutcome.Opened:
                await _signIn.SignInAsync(user!, isPersistent: false);
                return RedirectToAction("Index", "Dashboard");
            case ConfirmOutcome.WaitingForApproval:
                return StatusPage(new SignupStatusPage(w, language, "pending"));
            case ConfirmOutcome.AlreadyConfirmed:
                TempData["Success"] = w.VerifyAlready;
                return RedirectToAction("Login", "Account");
            default:
                return StatusPage(new SignupStatusPage(w, language, "invalid"));
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<IActionResult> FormAsync(SignupForm form, Dictionary<string, string> errors, string? problem, bool busy)
    {
        var language = form.Lang ?? "en";
        var w = SignupText.For(language);
        var plan = PricingRules.Parse(form.M, form.B, form.I);
        var quote = PricingRules.Quote(plan, await _plan.PriceTableAsync(DateTime.UtcNow), (await _plan.GetAsync()).Currency);
        var restaurant = SignupRules.Clean(form.Restaurant);
        (string, bool)? link = SignupRules.IsRestaurant(restaurant) ? await _slugs.SuggestAsync(restaurant) : null;

        ViewData["Lang"] = language;
        ViewData["PhotoAlt"] = w.Photo;
        ViewData["Title"] = w.SignupTitle;
        return View("Index", new SignupPage(w, language, form, plan, quote, await _signup.TrialDaysAsync(),
            _formTime.Protect(DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            MenuBase(), link, errors, problem ?? (busy ? w.ErrBusy : null), busy));
    }

    private IActionResult StatusPage(SignupStatusPage page)
    {
        ViewData["Lang"] = page.Lang;
        ViewData["PhotoAlt"] = page.W.Photo;
        ViewData["Title"] = page.Kind switch
        {
            "check" => page.W.CheckTitle,
            "confirm" or "invalid" => page.W.VerifyTitle,
            "pending" => page.W.PendingTitle,
            _ => page.W.SignupTitle
        };
        ViewBag.ContactUrl = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Landing:ContactUrl"] is { Length: > 0 } c ? c : "https://4cs.al/";
        return View("Status", page);
    }

    private DateTime? ShownAt(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try { return new DateTime(long.Parse(_formTime.Unprotect(token), System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc); }
        catch (Exception) { return null; }
    }

    private string MenuBase() => _seo.Url("/menu/").Replace("https://", "").Replace("http://", "");

    private static string CooldownKey(string email) => "signup-resend:" + email.Trim().ToLowerInvariant();

    private SeoMetadata Seo(string title, string description, string language, string path) => new()
    {
        Title = $"{title} · My Quick Menu",
        Description = description,
        CanonicalUrl = _seo.Url(path) + (language == "en" ? "" : "?lang=" + language),
        Locale = language,
        Alternates = new[] { new SeoAlternate("en", _seo.Url(path)), new SeoAlternate("sq", _seo.Url(path) + "?lang=sq") }
    };
}
