namespace RestaurantMenu.Tests;

public class OrderRulesTests
{
    // Pizza €5: Size (required, one of Small +0 / Large +2), Extras (optional, up to 2 of Cheese +1, Ham +1.5, Egg +1).
    private static Product Pizza(bool available = true) => new()
    {
        Id = 1, Name = "Pizza", Description = "", Price = 5m, IsAvailable = available,
        OptionGroups = new List<ProductOptionGroup>
        {
            new()
            {
                Id = 10, Name = "Size", MinSelect = 1, MaxSelect = 1, DisplayOrder = 0,
                Options = new List<ProductOption>
                {
                    new() { Id = 11, Name = "Small", PriceDelta = 0m, DisplayOrder = 0 },
                    new() { Id = 12, Name = "Large", PriceDelta = 2m, DisplayOrder = 1 }
                }
            },
            new()
            {
                Id = 20, Name = "Extras", MinSelect = 0, MaxSelect = 2, DisplayOrder = 1,
                Options = new List<ProductOption>
                {
                    new() { Id = 21, Name = "Cheese", PriceDelta = 1m, DisplayOrder = 0 },
                    new() { Id = 22, Name = "Ham", PriceDelta = 1.5m, DisplayOrder = 1 },
                    new() { Id = 23, Name = "Egg", PriceDelta = 1m, DisplayOrder = 2 }
                }
            }
        }
    };

    private static Product Water(decimal price = 1m) => new() { Id = 2, Name = "Water", Description = "", Price = price };

    private static IReadOnlyDictionary<int, Product> Menu(params Product[] p) => p.ToDictionary(x => x.Id);

    private static OrderLineRequest Line(int id, int qty, params int[] options) => new() { Id = id, Qty = qty, Options = options.ToList() };

    private static readonly Func<Product, bool> Always = _ => true;

    [Fact]
    public void Prices_come_from_the_menu_with_options()
    {
        var (lines, problem, _) = OrderRules.Price(new[] { Line(1, 2, 12, 21) }, Menu(Pizza()), Always);

        Assert.Null(problem);
        var l = Assert.Single(lines);
        Assert.Equal(8m, l.UnitPrice);
        Assert.Equal(2, l.Quantity);
        Assert.Equal("Large, Cheese", l.Options);
        Assert.Equal("12,21", l.OptionIds);
    }

    [Fact]
    public void Identical_lines_are_merged_whatever_the_option_order()
    {
        var (lines, problem, _) = OrderRules.Price(new[] { Line(1, 1, 12, 21), Line(1, 2, 21, 12), Line(1, 1, 11) }, Menu(Pizza()), Always);

        Assert.Null(problem);
        Assert.Equal(2, lines.Count);
        Assert.Equal(3, lines.Single(l => l.OptionIds == "12,21").Quantity);
    }

    [Fact]
    public void Missing_required_choice_means_the_menu_changed()
    {
        Assert.Equal(OrderProblem.MenuChanged, OrderRules.Price(new[] { Line(1, 1, 21) }, Menu(Pizza()), Always).Problem);
    }

    [Fact]
    public void Too_many_extras_means_the_menu_changed()
    {
        Assert.Equal(OrderProblem.MenuChanged, OrderRules.Price(new[] { Line(1, 1, 11, 21, 22, 23) }, Menu(Pizza()), Always).Problem);
    }

    [Fact]
    public void Option_of_another_dish_or_unknown_dish_is_refused()
    {
        Assert.Equal(OrderProblem.MenuChanged, OrderRules.Price(new[] { Line(1, 1, 11, 999) }, Menu(Pizza()), Always).Problem);
        Assert.Equal(OrderProblem.MenuChanged, OrderRules.Price(new[] { Line(42, 1) }, Menu(Pizza()), Always).Problem);
    }

    [Fact]
    public void Duplicate_option_ids_are_invalid()
    {
        Assert.Equal(OrderProblem.Invalid, OrderRules.Price(new[] { Line(1, 1, 11, 21, 21) }, Menu(Pizza()), Always).Problem);
    }

    [Fact]
    public void Sold_out_and_not_served_dishes_are_named()
    {
        var sold = OrderRules.Price(new[] { Line(1, 1, 11), Line(2, 1) }, Menu(Pizza(available: false), Water()), Always);
        Assert.Equal(OrderProblem.NotAvailableNow, sold.Problem);
        Assert.Equal(new[] { 1 }, sold.UnavailableIds);
        Assert.Empty(sold.Lines);

        var notServed = OrderRules.Price(new[] { Line(2, 1) }, Menu(Water()), _ => false);
        Assert.Equal(OrderProblem.NotAvailableNow, notServed.Problem);
        Assert.Equal(new[] { 2 }, notServed.UnavailableIds);
    }

    [Fact]
    public void Limits()
    {
        var menu = Menu(Water());
        Assert.Equal(OrderProblem.Empty, OrderRules.Price(Array.Empty<OrderLineRequest>(), menu, Always).Problem);
        Assert.Equal(OrderProblem.Empty, OrderRules.Price(null, menu, Always).Problem);
        Assert.Equal(OrderProblem.Invalid, OrderRules.Price(new[] { Line(2, 0) }, menu, Always).Problem);
        Assert.Equal(OrderProblem.Invalid, OrderRules.Price(new[] { Line(2, OrderRules.MaxQuantityPerLine + 1) }, menu, Always).Problem);
        Assert.Equal(OrderProblem.TooBig, OrderRules.Price(Enumerable.Range(0, OrderRules.MaxLines + 1).Select(_ => Line(2, 1)).ToList(), menu, Always).Problem);
        Assert.Equal(OrderProblem.TooBig, OrderRules.Price(Enumerable.Range(0, 6).Select(_ => Line(2, 20)).ToList(), menu, Always).Problem);
    }

    [Fact]
    public void Price_never_goes_below_zero()
    {
        var p = Water(1m);
        p.OptionGroups = new List<ProductOptionGroup>
        {
            new() { Id = 30, Name = "Deal", MinSelect = 1, MaxSelect = 1, Options = new List<ProductOption> { new() { Id = 31, Name = "Free", PriceDelta = -3m } } }
        };
        Assert.Equal(0m, OrderRules.Price(new[] { Line(2, 1, 31) }, Menu(p), Always).Lines.Single().UnitPrice);
    }

    [Fact]
    public void Note_becomes_one_capped_line()
    {
        Assert.Equal("No onions please", OrderRules.CleanNote("No onions\r\n  please "));
        Assert.Equal(OrderRules.MaxNote, OrderRules.CleanNote(new string('a', 500))!.Length);
        Assert.Null(OrderRules.CleanNote(" \n "));
    }

    [Fact]
    public void Table_codes()
    {
        var code = OrderRules.NewCode();
        Assert.Equal(OrderRules.CodeLength, code.Length);
        Assert.DoesNotContain(code, c => "01OIL".Contains(c));

        Assert.True(OrderRules.CodeMatches("7QX4MP", " 7qx4mp "));
        Assert.False(OrderRules.CodeMatches("7QX4MP", "7QX4MQ"));
        Assert.False(OrderRules.CodeMatches("7QX4MP", "7QX4M"));
        Assert.False(OrderRules.CodeMatches("7QX4MP", null));
    }
}

public class ProductOptionsTests
{
    private static ProductOptions.GroupForm Group(string name, bool required, string max, params (string Name, string Price)[] options) => new()
    {
        Name = name, Required = required, Max = max,
        Options = options.Select(o => new ProductOptions.OptionForm { Name = o.Name, Price = o.Price }).ToList()
    };

    [Fact]
    public void Valid_groups_are_built()
    {
        var (groups, errors) = ProductOptions.Validate(new[] { Group("Size", true, "1", ("Small", ""), ("Large", "2,50")) });

        Assert.Empty(errors);
        var g = Assert.Single(groups);
        Assert.Equal(1, g.MinSelect);
        Assert.Equal(1, g.MaxSelect);
        Assert.Equal(2.5m, g.Options.Single(o => o.Name == "Large").PriceDelta);
    }

    [Fact]
    public void Choose_up_to_is_clamped_to_the_number_of_options()
    {
        var (groups, errors) = ProductOptions.Validate(new[] { Group("Extras", false, "9", ("Cheese", "1"), ("Ham", "1.5")) });
        Assert.Empty(errors);
        Assert.Equal(2, groups.Single().MaxSelect);
    }

    [Fact]
    public void Empty_rows_are_ignored()
    {
        var (groups, errors) = ProductOptions.Validate(new[] { Group("", false, "1", ("", "")) });
        Assert.Empty(errors);
        Assert.Empty(groups);
    }

    [Theory]
    [InlineData("", "Small", "1", "give the group a name")]
    [InlineData("Size", "Small", "1.555", "isn't a valid amount")]
    [InlineData("Size", "Small", "abc", "isn't a valid amount")]
    public void Problems_are_reported(string group, string option, string price, string message)
    {
        var (groups, errors) = ProductOptions.Validate(new[] { Group(group, false, "1", (option, price)) });
        Assert.Empty(groups);
        Assert.Contains(errors, e => e.Contains(message));
    }

    [Fact]
    public void Duplicate_names_are_refused()
    {
        var (_, errors) = ProductOptions.Validate(new[]
        {
            Group("Size", true, "1", ("Small", ""), ("small", "1")),
            Group("size", false, "1", ("X", ""))
        });
        Assert.Contains(errors, e => e.Contains("two options have the same name"));
        Assert.Contains(errors, e => e.Contains("Two option groups have the same name"));
    }

    [Fact]
    public void From_price_uses_the_cheapest_required_choice()
    {
        var p = new Product
        {
            Name = "Calzone", Description = "", Price = 5m,
            OptionGroups = new List<ProductOptionGroup>
            {
                new() { MinSelect = 1, MaxSelect = 1, Options = new List<ProductOption> { new() { PriceDelta = 0.5m }, new() { PriceDelta = 2m } } }
            }
        };
        Assert.Equal(5.5m, ProductOptions.FromPrice(p));
        Assert.Null(ProductOptions.FromPrice(new Product { Name = "Water", Description = "", Price = 1m }));
    }
}
