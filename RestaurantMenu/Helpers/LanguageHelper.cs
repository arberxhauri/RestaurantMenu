using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantMenu.Helpers;

public static class LanguageHelper
{
    public static List<SelectListItem> GetAvailableLanguages()
    {
        return new List<SelectListItem>
        {
            new SelectListItem { Value = "en", Text = "English" },
            new SelectListItem { Value = "sq", Text = "Shqip (Albanian)" },
            new SelectListItem { Value = "de", Text = "Deutsch (German)" },
            new SelectListItem { Value = "fr", Text = "Français (French)" },
            new SelectListItem { Value = "it", Text = "Italiano (Italian)" },
            new SelectListItem { Value = "es", Text = "Español (Spanish)" },
            new SelectListItem { Value = "tr", Text = "Türkçe (Turkish)" }
        };
    }
        
    public static string GetLanguageName(string code)
    {
        return code switch
        {
            "en" => "English",
            "sq" => "Shqip",
            "de" => "Deutsch",
            "fr" => "Français",
            "it" => "Italiano",
            "es" => "Español",
            "tr" => "Türkçe",
            _ => code
        };
    }
}