namespace RestaurantMenu.ViewModels;

/// <summary>
/// The "Fill translations" bar on a form. <paramref name="Sources"/> maps each field
/// (name, description, nutritions) to the CSS selector of its English input; the
/// suggestions go into the form's translation_{field}_{lang} inputs.
/// </summary>
public record TranslateBarModel(string Kind, int BranchId, string[] Languages, IReadOnlyDictionary<string, string> Sources);
