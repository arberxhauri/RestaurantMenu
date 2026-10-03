using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// Guest-facing names for allergens and dietary tags, plus the filter sheet's wording,
/// in every menu language. Allergen names follow the short forms used in each language's
/// version of EU Regulation 1169/2011, Annex II. Have a native speaker review changes.
/// </summary>
public static class DietaryText
{
    public record Ui(
        string Filters,          // filter button and sheet title
        string ShowOnly,         // diet group
        string HideContaining,   // allergen group
        string Contains,         // "Contains: gluten, milk"
        string NoneDeclared,     // allergens declared as none
        string Unknown,          // allergens not declared
        string UnknownHidden,    // note in the sheet while an allergen filter is on
        string Clear,
        string ShowDishes,       // "Show {0} dishes"
        string Allergens,        // heading in the dish sheet
        string TellServer,       // reminder under the allergen filter
        string Without,          // active-filter summary: "Without: milk, eggs"
        string ShowOneDish);     // singular of ShowDishes

    // Order matches Dietary.AllAllergens (bit order).
    private static readonly Dictionary<string, string[]> AllergenNames = new()
    {
        ["en"] = new[] { "Gluten", "Crustaceans", "Eggs", "Fish", "Peanuts", "Soy", "Milk", "Nuts", "Celery", "Mustard", "Sesame", "Sulphites", "Lupin", "Molluscs" },
        ["sq"] = new[] { "Gluten", "Krustace", "Vezë", "Peshk", "Kikirikë", "Soja", "Qumësht", "Arra", "Selino", "Mustardë", "Susam", "Sulfite", "Lupinë", "Molusqe" },
        ["it"] = new[] { "Glutine", "Crostacei", "Uova", "Pesce", "Arachidi", "Soia", "Latte", "Frutta a guscio", "Sedano", "Senape", "Sesamo", "Solfiti", "Lupini", "Molluschi" },
        ["de"] = new[] { "Gluten", "Krebstiere", "Eier", "Fisch", "Erdnüsse", "Soja", "Milch", "Schalenfrüchte", "Sellerie", "Senf", "Sesam", "Sulfite", "Lupinen", "Weichtiere" },
        ["fr"] = new[] { "Gluten", "Crustacés", "Œufs", "Poisson", "Arachides", "Soja", "Lait", "Fruits à coque", "Céleri", "Moutarde", "Sésame", "Sulfites", "Lupin", "Mollusques" },
        ["es"] = new[] { "Gluten", "Crustáceos", "Huevos", "Pescado", "Cacahuetes", "Soja", "Leche", "Frutos de cáscara", "Apio", "Mostaza", "Sésamo", "Sulfitos", "Altramuces", "Moluscos" },
        ["tr"] = new[] { "Gluten", "Kabuklular", "Yumurta", "Balık", "Yer fıstığı", "Soya", "Süt", "Kabuklu yemişler", "Kereviz", "Hardal", "Susam", "Sülfitler", "Acı bakla", "Yumuşakçalar" }
    };

    // Order matches Dietary.AllDiets: Vegan, Vegetarian, Spicy, GlutenFree, Halal.
    private static readonly Dictionary<string, string[]> DietNames = new()
    {
        ["en"] = new[] { "Vegan", "Vegetarian", "Spicy", "Gluten-free", "Halal" },
        ["sq"] = new[] { "Vegan", "Vegjetarian", "Pikant", "Pa gluten", "Hallall" },
        ["it"] = new[] { "Vegano", "Vegetariano", "Piccante", "Senza glutine", "Halal" },
        ["de"] = new[] { "Vegan", "Vegetarisch", "Scharf", "Glutenfrei", "Halal" },
        ["fr"] = new[] { "Végan", "Végétarien", "Épicé", "Sans gluten", "Halal" },
        ["es"] = new[] { "Vegano", "Vegetariano", "Picante", "Sin gluten", "Halal" },
        ["tr"] = new[] { "Vegan", "Vejetaryen", "Acı", "Glutensiz", "Helal" }
    };

    private static readonly Dictionary<string, Ui> UiText = new()
    {
        ["en"] = new("Dietary & allergens", "Show only", "Hide dishes containing", "Contains", "None of the 14 major allergens", "Allergen information not provided. Please ask your server.", "Dishes without allergen information are hidden while an allergen is selected.", "Clear", "Show {0} dishes", "Allergens", "Always tell your server about allergies.", "Without", "Show 1 dish"),
        ["sq"] = new("Dieta & alergjenë", "Shfaq vetëm", "Fshih pjatat që përmbajnë", "Përmban", "Asnjë nga 14 alergjenët kryesorë", "Informacioni për alergjenët mungon. Ju lutemi pyesni kamarierin.", "Pjatat pa informacion për alergjenët fshihen kur zgjidhni një alergjen.", "Pastro", "Shfaq {0} pjata", "Alergjenët", "Gjithmonë njoftoni kamarierin për alergjitë.", "Pa", "Shfaq 1 pjatë"),
        ["it"] = new("Dieta e allergeni", "Mostra solo", "Nascondi piatti con", "Contiene", "Nessuno dei 14 allergeni principali", "Informazioni sugli allergeni non disponibili. Chiedi al personale.", "I piatti senza informazioni sugli allergeni sono nascosti quando selezioni un allergene.", "Azzera", "Mostra {0} piatti", "Allergeni", "Segnala sempre le allergie al personale.", "Senza", "Mostra 1 piatto"),
        ["de"] = new("Ernährung & Allergene", "Nur anzeigen", "Gerichte ausblenden mit", "Enthält", "Keines der 14 Hauptallergene", "Keine Allergenangaben. Bitte fragen Sie das Personal.", "Gerichte ohne Allergenangaben werden ausgeblendet, solange ein Allergen gewählt ist.", "Zurücksetzen", "{0} Gerichte anzeigen", "Allergene", "Informieren Sie das Personal immer über Allergien.", "Ohne", "1 Gericht anzeigen"),
        ["fr"] = new("Régimes & allergènes", "Afficher seulement", "Masquer les plats contenant", "Contient", "Aucun des 14 allergènes majeurs", "Informations sur les allergènes non fournies. Demandez au personnel.", "Les plats sans informations sur les allergènes sont masqués tant qu'un allergène est sélectionné.", "Effacer", "Afficher {0} plats", "Allergènes", "Signalez toujours vos allergies au personnel.", "Sans", "Afficher 1 plat"),
        ["es"] = new("Dieta y alérgenos", "Mostrar solo", "Ocultar platos con", "Contiene", "Ninguno de los 14 alérgenos principales", "Información sobre alérgenos no disponible. Pregunta al personal.", "Los platos sin información sobre alérgenos se ocultan mientras haya un alérgeno seleccionado.", "Borrar", "Mostrar {0} platos", "Alérgenos", "Informa siempre al personal sobre tus alergias.", "Sin", "Mostrar 1 plato"),
        ["tr"] = new("Diyet ve alerjenler", "Yalnızca göster", "Şunları içerenleri gizle", "İçerir", "14 ana alerjenden hiçbiri", "Alerjen bilgisi verilmemiş. Lütfen garsona sorun.", "Bir alerjen seçiliyken alerjen bilgisi olmayan yemekler gizlenir.", "Temizle", "{0} yemeği göster", "Alerjenler", "Alerjilerinizi her zaman garsona bildirin.", "Hariç", "1 yemeği göster")
    };

    private static string Lang(string? language) => language != null && UiText.ContainsKey(language) ? language : "en";

    public static Ui For(string? language) => UiText[Lang(language)];

    public static string Name(Allergen allergen, string? language) =>
        AllergenNames[Lang(language)][Array.IndexOf(Dietary.AllAllergens, allergen)];

    public static string Name(Diet diet, string? language) =>
        DietNames[Lang(language)][Array.IndexOf(Dietary.AllDiets, diet)];

    /// <summary>The declared allergens of a dish as translated names, in a fixed order.</summary>
    public static List<string> Names(Allergen allergens, string? language) =>
        Dietary.AllAllergens.Where(a => allergens.HasFlag(a)).Select(a => Name(a, language)).ToList();

    /// <summary>Phosphor icon per diet tag.</summary>
    public static string Icon(Diet diet) => diet switch
    {
        Diet.Vegan => "ph-leaf",
        Diet.Vegetarian => "ph-carrot",
        Diet.Spicy => "ph-pepper",
        Diet.GlutenFree => "ph-grains-slash",
        _ => "ph-seal-check"
    };
}
