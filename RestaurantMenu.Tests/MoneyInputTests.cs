namespace RestaurantMenu.Tests;

public class MoneyInputTests
{
    [Theory]
    [InlineData("15", 1500)]
    [InlineData("15.5", 1550)]
    [InlineData("15.50", 1550)]
    [InlineData("15,50", 1550)]
    [InlineData(" 1 500 ", 150000)]
    [InlineData("€9", 900)]
    [InlineData("0", 0)]
    public void Amounts_become_cents(string text, int cents)
    {
        Assert.True(MoneyInput.TryParseCents(text, out var value));
        Assert.Equal(cents, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_box_means_no_price(string? text)
    {
        Assert.True(MoneyInput.TryParseCents(text, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("15.555")]
    [InlineData("1.500,00")]
    [InlineData("15.")]
    [InlineData("99999999")]
    public void Anything_else_is_refused(string text)
    {
        Assert.False(MoneyInput.TryParseCents(text, out _));
    }

    [Fact]
    public void Cents_format_back_for_the_box()
    {
        Assert.Equal("15.00", MoneyInput.Format(1500));
        Assert.Equal("", MoneyInput.Format(null));
    }
}
