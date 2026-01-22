using Microsoft.AspNetCore.Mvc.Rendering;

namespace RestaurantMenu.Helpers;

public static class CurrencyHelper
    {
        public static List<SelectListItem> GetCurrencies()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "USD", Text = "USD - US Dollar ($)" },
                new SelectListItem { Value = "EUR", Text = "EUR - Euro (€)" },
                new SelectListItem { Value = "GBP", Text = "GBP - British Pound (£)" },
                new SelectListItem { Value = "ALL", Text = "ALL - Albanian Lek (L)" },
                new SelectListItem { Value = "CHF", Text = "CHF - Swiss Franc (CHF)" },
                new SelectListItem { Value = "CAD", Text = "CAD - Canadian Dollar ($)" },
                new SelectListItem { Value = "AUD", Text = "AUD - Australian Dollar ($)" },
                new SelectListItem { Value = "JPY", Text = "JPY - Japanese Yen (¥)" },
                new SelectListItem { Value = "CNY", Text = "CNY - Chinese Yuan (¥)" },
                new SelectListItem { Value = "INR", Text = "INR - Indian Rupee (₹)" },
                new SelectListItem { Value = "TRY", Text = "TRY - Turkish Lira (₺)" },
                new SelectListItem { Value = "MXN", Text = "MXN - Mexican Peso ($)" },
                new SelectListItem { Value = "BRL", Text = "BRL - Brazilian Real (R$)" },
                new SelectListItem { Value = "ZAR", Text = "ZAR - South African Rand (R)" },
                new SelectListItem { Value = "AED", Text = "AED - UAE Dirham (د.إ)" },
                new SelectListItem { Value = "SAR", Text = "SAR - Saudi Riyal (﷼)" },
                new SelectListItem { Value = "RUB", Text = "RUB - Russian Ruble (₽)" },
                new SelectListItem { Value = "SEK", Text = "SEK - Swedish Krona (kr)" },
                new SelectListItem { Value = "NOK", Text = "NOK - Norwegian Krone (kr)" },
                new SelectListItem { Value = "DKK", Text = "DKK - Danish Krone (kr)" },
                new SelectListItem { Value = "PLN", Text = "PLN - Polish Zloty (zł)" },
                new SelectListItem { Value = "CZK", Text = "CZK - Czech Koruna (Kč)" },
                new SelectListItem { Value = "HUF", Text = "HUF - Hungarian Forint (Ft)" },
                new SelectListItem { Value = "RON", Text = "RON - Romanian Leu (lei)" },
                new SelectListItem { Value = "BGN", Text = "BGN - Bulgarian Lev (лв)" },
                new SelectListItem { Value = "HRK", Text = "HRK - Croatian Kuna (kn)" }
            };
        }

        public static string GetCurrencySymbol(string currencyCode)
        {
            return currencyCode switch
            {
                "USD" or "CAD" or "AUD" or "MXN" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "ALL" => "L",
                "CHF" => "CHF",
                "JPY" or "CNY" => "¥",
                "INR" => "₹",
                "TRY" => "₺",
                "BRL" => "R$",
                "ZAR" => "R",
                "AED" => "د.إ",
                "SAR" => "﷼",
                "RUB" => "₽",
                "SEK" or "NOK" or "DKK" => "kr",
                "PLN" => "zł",
                "CZK" => "Kč",
                "HUF" => "Ft",
                "RON" => "lei",
                "BGN" => "лв",
                "HRK" => "kn",
                _ => currencyCode
            };
        }
    }