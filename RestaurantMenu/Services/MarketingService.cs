using Microsoft.Extensions.Options;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>What the marketing pages' header and footer need: language, links and the call to action.</summary>
public record MarketingChrome(
    MarketingText T,
    string Lang,
    bool SignedIn,
    bool SignupOpen,
    int TrialDays,
    string CtaHref,
    string CtaLabel,
    string ContactHref,
    string OtherLanguageHref,
    BrandOptions Brand)
{
    /// <summary>A marketing path in this page's language: "/pricing" or "/pricing?lang=sq".</summary>
    public string Link(string path) => path + SeoLanguages.Query(Lang, "en");
}

/// <summary>Prices for the marketing pages, schema.org offers and llms.txt, always read from the price book.</summary>
public record MarketingPrices(string Currency, IReadOnlyList<(string Key, Quote Quote)> Presets)
{
    /// <summary>The cheapest complete monthly preset ("€5"), or null when no prices are set.</summary>
    public string? Lowest => Presets.Where(p => p.Quote.Complete).OrderBy(p => p.Quote.PerMonthCents)
        .Select(p => PricingRules.Money(p.Quote.PerMonthCents, Currency)).FirstOrDefault();
}

/// <summary>
/// Shared by the home page, the landing pages, pricing, llms.txt and the Organization schema, so
/// the definition, prices and calls to action read the same everywhere.
/// </summary>
public class MarketingService
{
    private readonly SignupService _signup;
    private readonly PlanSettingsService _plans;
    private readonly IConfiguration _config;
    private readonly BrandOptions _brand;
    private readonly IHttpContextAccessor _http;

    public MarketingService(SignupService signup, PlanSettingsService plans, IConfiguration config, IOptions<BrandOptions> brand, IHttpContextAccessor http)
    {
        _signup = signup;
        _plans = plans;
        _config = config;
        _brand = brand.Value;
        _http = http;
    }

    public BrandOptions Brand => _brand;

    /// <summary>Where "Get in touch" goes while self-serve signup is closed.</summary>
    public string ContactHref(string lang) =>
        _config["Landing:ContactUrl"] is { Length: > 0 } configured ? configured : "/contact" + SeoLanguages.Query(lang, "en");

    public async Task<MarketingPrices> PricesAsync()
    {
        var table = await _plans.PriceTableAsync(DateTime.UtcNow);
        var currency = (await _plans.GetAsync()).Currency;
        var presets = PricingRules.Presets
            .Select(p => (p.Key, PricingRules.Quote(PricingRules.Parse(null, "1", "Month", p.Key), table, currency)))
            .ToList();
        return new MarketingPrices(currency, presets);
    }

    /// <summary>The one-sentence definition with the current lowest price.</summary>
    public async Task<string> DefinitionAsync(string lang)
    {
        var t = MarketingText.For(lang);
        return (await PricesAsync()).Lowest is { } lowest ? string.Format(t.Definition, lowest) : t.DefinitionNoPrice;
    }

    /// <param name="path">This page's path without ?lang (for the language switch).</param>
    public async Task<MarketingChrome> ChromeAsync(string lang, string path)
    {
        var t = MarketingText.For(lang);
        var signedIn = _http.HttpContext?.User.Identity?.IsAuthenticated == true;
        var signupOpen = !signedIn && await _signup.IsOpenAsync();
        var trialDays = await _signup.TrialDaysAsync();
        var contact = ContactHref(lang);
        var langQuery = SeoLanguages.Query(lang, "en");
        var (label, href) = signedIn ? (t.Dashboard, "/dashboard")
            : signupOpen ? (t.StartTrial, "/pricing" + langQuery)
            : (t.GetInTouch, contact);
        return new MarketingChrome(t, lang, signedIn, signupOpen, trialDays, href, label, contact,
            path + SeoLanguages.Query(t.OtherLanguageCode, "en"), _brand);
    }
}
