using System.Net;
using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// Caddy's on-demand TLS asks the app before getting a certificate for a host it has no
/// certificate for yet. Only restaurants' hosts may get one, and only Caddy may ask.
/// </summary>
public class TlsAskRulesTests
{
    private static SiteTarget Target(bool verified) =>
        new(1, "oliva", "www.oliva.al", IsCustomDomain: true, Verified: verified, Published: true, Token: "t");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("172.18.0.5")]       // Docker bridge network (Caddy's container)
    [InlineData("10.0.0.7")]
    [InlineData("192.168.1.20")]
    [InlineData("::ffff:172.18.0.5")] // IPv4-mapped, as Kestrel may report it
    [InlineData("fd12:3456::1")]      // IPv6 unique local
    public void Callers_on_the_server_or_docker_network_are_trusted(string address) =>
        Assert.True(TlsAskRules.IsTrustedCaller(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("138.199.131.201")]
    [InlineData("172.32.0.1")]
    [InlineData("2a01:4f8:c010:8c3b::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_callers_are_not_trusted(string address) =>
        Assert.False(TlsAskRules.IsTrustedCaller(IPAddress.Parse(address)));

    [Fact]
    public void An_unknown_caller_is_not_trusted() => Assert.False(TlsAskRules.IsTrustedCaller(null));

    [Theory]
    [InlineData("www.Oliva.al", "www.oliva.al")]
    [InlineData("oliva.al.", "oliva.al")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("138.199.131.201", null)]
    [InlineData("localhost", null)]
    [InlineData("mqm.internal", null)]
    public void The_asked_domain_is_normalised_or_refused(string? domain, string? expected) =>
        Assert.Equal(expected, TlsAskRules.Candidate(domain));

    [Theory]
    [InlineData("www.oliva.al", "oliva.al")]
    [InlineData("oliva.al", "www.oliva.al")]
    public void The_twin_of_a_host_is_its_www_or_bare_form(string host, string twin) =>
        Assert.Equal(twin, TlsAskRules.Twin(host));

    [Fact]
    public void A_connected_host_gets_a_certificate_even_before_it_is_verified() =>
        // The domain check reaches the site over https after Caddy's http → https redirect.
        Assert.True(TlsAskRules.Allows(Target(verified: false), twin: null));

    [Fact]
    public void The_twin_of_a_verified_domain_gets_a_certificate_for_its_redirect() =>
        Assert.True(TlsAskRules.Allows(exact: null, twin: Target(verified: true)));

    [Fact]
    public void The_twin_of_an_unverified_domain_gets_no_certificate() =>
        Assert.False(TlsAskRules.Allows(exact: null, twin: Target(verified: false)));

    [Fact]
    public void An_unknown_host_gets_no_certificate() =>
        Assert.False(TlsAskRules.Allows(exact: null, twin: null));
}
