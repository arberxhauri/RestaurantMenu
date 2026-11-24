using RestaurantMenu.Services;

namespace RestaurantMenu.Tests;

/// <summary>
/// Station devices (docs/PLAN-backoffice.md §5, phase 8): a shared kitchen or bar tablet signs in with a
/// one-time pairing code instead of someone's password.
/// </summary>
public class StationDeviceRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_code_is_eight_unambiguous_characters()
    {
        var code = StationDeviceRules.NewCode();

        Assert.Equal(StationDeviceRules.CodeLength, code.Length);
        Assert.All(code, c => Assert.Contains(c, StationDeviceRules.CodeAlphabet));
        Assert.DoesNotContain('O', StationDeviceRules.CodeAlphabet);
        Assert.DoesNotContain('0', StationDeviceRules.CodeAlphabet);
        Assert.DoesNotContain('I', StationDeviceRules.CodeAlphabet);
        Assert.DoesNotContain('1', StationDeviceRules.CodeAlphabet);
    }

    [Fact]
    public void Codes_are_not_repeated()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => StationDeviceRules.NewCode()).ToHashSet();
        Assert.Equal(200, codes.Count);
    }

    [Theory]
    [InlineData("abcd-efgh", "ABCDEFGH")]
    [InlineData(" ABCD EFGH ", "ABCDEFGH")]
    [InlineData("ABCDEFGH", "ABCDEFGH")]
    [InlineData("k7m2-p9qx", "K7M2P9QX")]
    public void A_typed_code_is_read_the_way_people_type_it(string typed, string expected) =>
        Assert.Equal(expected, StationDeviceRules.NormalizeCode(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFG")]
    [InlineData("ABCD-EFGHJ")]
    [InlineData("ABCD-EFG0")]
    [InlineData("ABCD-EFGÉ")]
    public void A_code_of_the_wrong_shape_is_refused(string? typed) =>
        Assert.Null(StationDeviceRules.NormalizeCode(typed));

    [Fact]
    public void A_code_is_shown_in_two_halves() =>
        Assert.Equal("ABCD-EFGH", StationDeviceRules.FormatCode("ABCDEFGH"));

    [Fact]
    public void Only_the_hash_of_a_code_is_kept_and_it_is_stable()
    {
        var hash = StationDeviceRules.HashCode("ABCDEFGH");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, StationDeviceRules.HashCode("ABCDEFGH"));
        Assert.NotEqual(hash, StationDeviceRules.HashCode("ABCDEFGJ"));
        Assert.DoesNotContain("ABCDEFGH", hash);
    }

    [Fact]
    public void A_code_works_until_it_expires()
    {
        Assert.True(StationDeviceRules.CanPair("hash", Now.AddMinutes(1), Now));
        Assert.False(StationDeviceRules.CanPair("hash", Now, Now));
        Assert.False(StationDeviceRules.CanPair("hash", Now.AddMinutes(-1), Now));
        Assert.False(StationDeviceRules.CanPair(null, Now.AddMinutes(10), Now));
        Assert.False(StationDeviceRules.CanPair("hash", null, Now));
    }

    [Fact]
    public void A_code_lasts_thirty_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(30), StationDeviceRules.PairingLifetime);

    [Theory]
    [InlineData(ServiceStation.Kitchen, BranchRole.Kitchen)]
    [InlineData(ServiceStation.Bar, BranchRole.Bar)]
    [InlineData(ServiceStation.None, BranchRole.None)]
    [InlineData(ServiceStation.Kitchen | ServiceStation.Bar, BranchRole.None)]
    public void A_device_acts_with_its_station_role_only(ServiceStation station, BranchRole role) =>
        Assert.Equal(role, StationDeviceRules.RoleFor(station));

    [Theory]
    [InlineData(ServiceStation.Kitchen, true)]
    [InlineData(ServiceStation.Bar, true)]
    [InlineData(ServiceStation.None, false)]
    [InlineData((ServiceStation)3, false)]
    public void Devices_are_for_the_kitchen_or_the_bar(ServiceStation station, bool allowed) =>
        Assert.Equal(allowed, StationDeviceRules.IsDeviceStation(station));

    [Theory]
    [InlineData(ServiceStation.Kitchen, "/kitchen/4")]
    [InlineData(ServiceStation.Bar, "/kitchen/4/bar")]
    public void A_paired_device_opens_its_station_screen(ServiceStation station, string path) =>
        Assert.Equal(path, StationDeviceRules.ScreenPath(4, station));

    [Fact]
    public void Last_seen_is_written_at_most_every_five_minutes()
    {
        Assert.True(StationDeviceRules.ShouldRecordSeen(null, Now));
        Assert.True(StationDeviceRules.ShouldRecordSeen(Now.AddMinutes(-5), Now));
        Assert.False(StationDeviceRules.ShouldRecordSeen(Now.AddMinutes(-4), Now));
    }

    [Theory]
    [InlineData("  Kitchen tablet  ", "Kitchen tablet")]
    [InlineData("Bar", "Bar")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_device_name_is_trimmed_and_required(string? typed, string? expected) =>
        Assert.Equal(expected, StationDeviceRules.CleanName(typed));

    [Fact]
    public void A_long_device_name_is_cut_to_the_limit() =>
        Assert.Equal(StationDeviceRules.MaxNameLength, StationDeviceRules.CleanName(new string('x', 200))!.Length);

    [Fact]
    public void A_paired_device_is_connected()
    {
        var device = new StationDevice { SessionStamp = "stamp", PairingCodeHash = null };
        Assert.Equal(StationDeviceState.Paired, StationDeviceRules.State(device, Now));
    }

    [Fact]
    public void A_device_with_a_live_code_is_waiting_and_one_with_an_old_code_needs_a_new_one()
    {
        var waiting = new StationDevice { PairingCodeHash = "h", PairingExpiresUtc = Now.AddMinutes(5) };
        var expired = new StationDevice { PairingCodeHash = "h", PairingExpiresUtc = Now.AddMinutes(-5) };

        Assert.Equal(StationDeviceState.Waiting, StationDeviceRules.State(waiting, Now));
        Assert.Equal(StationDeviceState.CodeExpired, StationDeviceRules.State(expired, Now));
    }

    [Fact]
    public void A_signed_in_device_only_stays_signed_in_with_the_current_stamp()
    {
        var device = new StationDevice { SessionStamp = "current" };

        Assert.True(StationDeviceRules.SessionValid(device, "current"));
        Assert.False(StationDeviceRules.SessionValid(device, "old"));
        Assert.False(StationDeviceRules.SessionValid(device, null));
        Assert.False(StationDeviceRules.SessionValid(new StationDevice { SessionStamp = null }, null));
        Assert.False(StationDeviceRules.SessionValid(null, "current"));
    }
}

/// <summary>Live screens of a device, cut off when it is removed or re-paired.</summary>
public class StationConnectionsTests
{
    [Fact]
    public void Taking_a_device_returns_only_its_connections_once()
    {
        var connections = new StationConnections();
        connections.Track(1, "a");
        connections.Track(1, "b");
        connections.Track(2, "c");

        Assert.Equal(new[] { "a", "b" }, connections.Take(1).OrderBy(c => c));
        Assert.Empty(connections.Take(1));
        Assert.Equal(new[] { "c" }, connections.Take(2));
    }

    [Fact]
    public void A_closed_connection_is_forgotten()
    {
        var connections = new StationConnections();
        connections.Track(1, "a");

        connections.Forget("a");

        Assert.Empty(connections.Take(1));
    }
}
