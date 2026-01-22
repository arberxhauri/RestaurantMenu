using System.Text.Json;

namespace RestaurantMenu.Helpers
{
    public static class TranslationHelper
    {
        public static string GetTranslation(string defaultText, string? translationsJson, string language)
        {
            if (language == "en" || string.IsNullOrEmpty(translationsJson))
            {
                return defaultText;
            }

            try
            {
                var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(translationsJson);
                if (translations != null && translations.ContainsKey(language))
                {
                    return translations[language];
                }
            }
            catch
            {
                // Return default if JSON parsing fails
            }

            return defaultText;
        }

        public static string SetTranslation(string? existingJson, string language, string translation)
        {
            Dictionary<string, string> translations;

            if (string.IsNullOrEmpty(existingJson))
            {
                translations = new Dictionary<string, string>();
            }
            else
            {
                try
                {
                    translations = JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson) 
                                   ?? new Dictionary<string, string>();
                }
                catch
                {
                    translations = new Dictionary<string, string>();
                }
            }

            translations[language] = translation;
            return JsonSerializer.Serialize(translations);
        }
    }
}