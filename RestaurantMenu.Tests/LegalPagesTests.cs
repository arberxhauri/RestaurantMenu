using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// The legal pages a payment provider (Paddle) checks before approving the site: terms, privacy and a
/// refund policy, reachable from every marketing page and naming the company that sells the service.
/// </summary>
public class LegalPagesTests
{
    [Theory]
    [InlineData("privacy", "/home/privacy")]
    [InlineData("terms", "/home/terms")]
    [InlineData("refunds", "/home/refunds")]
    public void Each_legal_page_is_listed_for_the_footer_and_the_sitemap(string key, string path) =>
        Assert.Contains(MarketingPages.All, p => p.Key == key && p.Path == path);

    [Fact]
    public void The_refund_policy_is_english_only_like_the_other_legal_pages() =>
        Assert.Equal(new[] { "en" }, MarketingPages.Get("refunds").Languages);

    [Theory]
    [InlineData("en", "Refunds")]
    [InlineData("sq", "Rimbursimet")]
    public void The_footer_names_the_refund_policy_in_each_language(string language, string label) =>
        Assert.Equal(label, MarketingText.For(language).Refunds);

    [Fact]
    public void A_refunds_subdomain_or_branch_link_is_reserved() =>
        Assert.False(SlugRules.IsValid("refunds"));

    [Fact]
    public void The_seller_is_the_parent_company_until_a_legal_name_is_set() =>
        Assert.Equal("4CS", new BrandOptions().LegalEntity);

    [Theory]
    [InlineData("4CS SH.P.K.", "4CS SH.P.K.")]
    [InlineData("  4CS SH.P.K.  ", "4CS SH.P.K.")]
    [InlineData("   ", "4CS")]
    public void The_registered_legal_name_wins_when_set(string legalName, string expected) =>
        Assert.Equal(expected, new BrandOptions { LegalName = legalName }.LegalEntity);

    [Fact]
    public void The_terms_version_accepted_at_signup_is_the_new_terms_date() =>
        Assert.Equal("2026-11-23", TermsInfo.Version);
}
