namespace RestaurantMenu.Tests;

public class SignupRulesTests
{
    [Theory]
    [InlineData("Ana@Oliva.AL", "oliva.al")]
    [InlineData("x@sub.example.com", "sub.example.com")]
    [InlineData("nobody", null)]
    [InlineData("trailing@", null)]
    [InlineData(null, null)]
    public void Domain_of_an_address(string? email, string? domain)
    {
        Assert.Equal(domain, SignupRules.Domain(email));
    }

    [Theory]
    [InlineData("oliva.al", true)]
    [InlineData("gmail.com", false)]
    [InlineData("Hotmail.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Webmail_is_not_a_company_domain(string? domain, bool company)
    {
        Assert.Equal(company, SignupRules.IsCompanyDomain(domain));
    }

    [Theory]
    [InlineData("ana@oliva.al", true)]
    [InlineData("ana@localhost", false)]
    [InlineData("not an email", false)]
    [InlineData("", false)]
    public void Email_check(string email, bool ok)
    {
        Assert.Equal(ok, SignupRules.IsEmail(email));
    }

    [Theory]
    [InlineData("Restorant1", true)]
    [InlineData("restorant1", false)]
    [InlineData("RESTORANT1", false)]
    [InlineData("Restorant", false)]
    [InlineData("Ab1", false)]
    [InlineData(null, false)]
    public void Password_matches_the_identity_rules(string? password, bool ok)
    {
        Assert.Equal(ok, SignupRules.IsPassword(password));
    }

    [Fact]
    public void Text_is_trimmed_collapsed_and_stripped_of_control_characters()
    {
        Assert.Equal("Oliva Kitchen", SignupRules.Clean("  Oliva \t  Kitchen\n"));
        Assert.Equal("", SignupRules.Clean(null));
    }

    [Theory]
    [InlineData("Oliva", true)]
    [InlineData("O", false)]
    [InlineData("!!", false)]
    public void Restaurant_names_need_a_letter_or_digit(string name, bool ok)
    {
        Assert.Equal(ok, SignupRules.IsRestaurant(name));
    }

    [Theory]
    [InlineData("sq", null, "sq")]
    [InlineData("en", "sq-AL", "en")]
    [InlineData(null, "sq-AL,sq;q=0.9", "sq")]
    [InlineData(null, "it-IT,en;q=0.8", "en")]
    [InlineData("de", null, "en")]
    public void Funnel_language_from_the_link_or_the_browser(string? requested, string? accept, string expected)
    {
        Assert.Equal(expected, SignupText.Pick(requested, accept));
    }

    [Fact]
    public void Countries_are_the_same_codes_in_both_languages()
    {
        Assert.Equal(SignupText.CountriesFor("en").Select(c => c.Code), SignupText.CountriesFor("sq").Select(c => c.Code));
        Assert.True(SignupText.IsCountry("AL"));
        Assert.False(SignupText.IsCountry("US"));
    }
}
