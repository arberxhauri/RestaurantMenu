namespace RestaurantMenu.Models;

/// <summary>
/// The 14 allergens that EU Regulation 1169/2011 (Annex II) requires a menu to declare.
/// Stored as an int bit mask on <see cref="Product.Allergens"/>. The values are persisted,
/// so never renumber them; add new ones at the next free bit.
/// </summary>
[Flags]
public enum Allergen
{
    None = 0,
    Gluten = 1 << 0,
    Crustaceans = 1 << 1,
    Eggs = 1 << 2,
    Fish = 1 << 3,
    Peanuts = 1 << 4,
    Soy = 1 << 5,
    Milk = 1 << 6,
    Nuts = 1 << 7,
    Celery = 1 << 8,
    Mustard = 1 << 9,
    Sesame = 1 << 10,
    Sulphites = 1 << 11,
    Lupin = 1 << 12,
    Molluscs = 1 << 13
}

/// <summary>Dietary tags guests can filter by. Persisted as an int bit mask; never renumber.</summary>
[Flags]
public enum Diet
{
    None = 0,
    Vegan = 1 << 0,
    Vegetarian = 1 << 1,
    Spicy = 1 << 2,
    GlutenFree = 1 << 3,
    Halal = 1 << 4
}

/// <summary>Parsing and consistency rules shared by the dish form and the controller.</summary>
public static class Dietary
{
    public static readonly Allergen[] AllAllergens = Enum.GetValues<Allergen>().Where(a => a != Allergen.None).ToArray();
    public static readonly Diet[] AllDiets = Enum.GetValues<Diet>().Where(d => d != Diet.None).ToArray();

    private static readonly int AllergenMask = AllAllergens.Aggregate(0, (m, a) => m | (int)a);
    private static readonly int DietMask = AllDiets.Aggregate(0, (m, d) => m | (int)d);

    /// <summary>ORs the posted checkbox values, ignoring anything that isn't a known allergen.</summary>
    public static Allergen ToAllergens(IEnumerable<int>? values) =>
        (Allergen)((values ?? Array.Empty<int>()).Aggregate(0, (m, v) => m | v) & AllergenMask);

    /// <summary>ORs the posted checkbox values; vegan always implies vegetarian.</summary>
    public static Diet ToDiets(IEnumerable<int>? values)
    {
        var diets = (Diet)((values ?? Array.Empty<int>()).Aggregate(0, (m, v) => m | v) & DietMask);
        return diets.HasFlag(Diet.Vegan) ? diets | Diet.Vegetarian : diets;
    }

    /// <summary>
    /// Combinations a guest could be hurt by trusting, e.g. "vegan" with milk. Returns
    /// one message per problem; empty when the declaration is consistent.
    /// </summary>
    public static List<string> Conflicts(Allergen? allergens, Diet diets)
    {
        var problems = new List<string>();
        var a = allergens ?? Allergen.None;

        var notVegan = a & (Allergen.Milk | Allergen.Eggs | Allergen.Fish | Allergen.Crustaceans | Allergen.Molluscs);
        if (diets.HasFlag(Diet.Vegan) && notVegan != Allergen.None)
            problems.Add($"A vegan dish can't contain {List(notVegan)}.");

        var notVegetarian = a & (Allergen.Fish | Allergen.Crustaceans | Allergen.Molluscs);
        if (diets.HasFlag(Diet.Vegetarian) && !diets.HasFlag(Diet.Vegan) && notVegetarian != Allergen.None)
            problems.Add($"A vegetarian dish can't contain {List(notVegetarian)}.");

        if (diets.HasFlag(Diet.GlutenFree) && a.HasFlag(Allergen.Gluten))
            problems.Add("A gluten-free dish can't contain gluten.");

        return problems;
    }

    private static string List(Allergen a) =>
        string.Join(", ", AllAllergens.Where(x => a.HasFlag(x)).Select(x => x.ToString().ToLowerInvariant()));
}
