using System.Text.RegularExpressions;

namespace RestaurantMenu.Helpers;

/// <summary>Guest feedback: input rules, the Google review link, and the guest-facing words.</summary>
public static class FeedbackRules
{
    public const int MaxComment = 1000;
    public const int MaxContact = 120;
    /// <summary>Ratings at or below this reach the owner privately (and by email).</summary>
    public const int LowRating = 3;

    // Only Google's own review addresses: the menu must never send guests somewhere else.
    private static readonly string[] GoogleHosts = { "google.com", "g.page", "maps.app.goo.gl", "goo.gl" };

    /// <summary>
    /// A Google review link the owner pasted, or a Place ID (ChIJ…), as a link guests can open;
    /// null if it isn't one. Accepts g.page/r/…/review, search.google.com/local/writereview?placeid=…,
    /// maps.app.goo.gl/… and Google Maps links.
    /// </summary>
    public static string? NormalizeGoogleReviewUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (Regex.IsMatch(s, @"^ChIJ[A-Za-z0-9_-]{10,}$"))
            return $"https://search.google.com/local/writereview?placeid={s}";
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || s.Length > 300) return null;
        var host = u.Host.ToLowerInvariant();
        var ok = GoogleHosts.Any(g => host == g || host.EndsWith("." + g)) || Regex.IsMatch(host, @"^(www\.)?google\.[a-z.]{2,6}$");
        return ok ? u.ToString() : null;
    }

    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Replace("\r\n", "\n").Trim();
        t = new string(t.Where(c => c == '\n' || !char.IsControl(c)).ToArray());
        while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    public static string Stars(int rating) => new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', 5 - Math.Clamp(rating, 0, 5));

    // ---------------------------------------------------------------- guest words

    public record Words(
        string Prompt,          // "How was your visit?"
        string Star, string StarsN, // aria: "1 star", "{0} stars"
        string LowTitle, string LowHint, string HighTitle, string CommentPlaceholder,
        string ContactLabel, string ContactHint, string Send, string Sending,
        string ThanksLow, string ThanksHigh, string GoogleAsk, string GoogleButton, string Done,
        string AlreadySent, string Error);

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new("How was your visit?", "1 star", "{0} stars",
            "Sorry to hear that. What went wrong?", "Only the restaurant reads this.", "Great! Anything you'd like to add?", "Your comments (optional)",
            "How can we reach you? (optional)", "Phone or email, if you'd like the restaurant to get back to you.", "Send", "Sending…",
            "Thank you. The restaurant will read this today.", "Thank you!", "Would you share it on Google? It helps a lot.", "Review on Google", "Close",
            "Thanks for your feedback today!", "Couldn't send. Please try again."),
        ["sq"] = new("Si ishte vizita juaj?", "1 yll", "{0} yje",
            "Na vjen keq. Çfarë nuk shkoi mirë?", "Këtë e lexon vetëm restoranti.", "Shkëlqyeshëm! Doni të shtoni diçka?", "Komentet tuaja (opsionale)",
            "Si mund t'ju kontaktojmë? (opsionale)", "Telefon ose email, nëse doni që restoranti t'ju kthejë përgjigje.", "Dërgo", "Po dërgohet…",
            "Faleminderit. Restoranti do ta lexojë sot.", "Faleminderit!", "A do ta ndanit në Google? Ndihmon shumë.", "Vlerëso në Google", "Mbyll",
            "Faleminderit për vlerësimin sot!", "Nuk u dërgua. Provoni përsëri."),
        ["it"] = new("Com'è andata la visita?", "1 stella", "{0} stelle",
            "Ci dispiace. Cosa non è andato bene?", "Lo legge solo il ristorante.", "Fantastico! Vuoi aggiungere qualcosa?", "I tuoi commenti (facoltativi)",
            "Come possiamo contattarti? (facoltativo)", "Telefono o email, se vuoi che il ristorante ti risponda.", "Invia", "Invio…",
            "Grazie. Il ristorante lo leggerà oggi stesso.", "Grazie!", "Lo condivideresti su Google? Aiuta molto.", "Recensisci su Google", "Chiudi",
            "Grazie per il tuo parere di oggi!", "Invio non riuscito. Riprova."),
        ["de"] = new("Wie war dein Besuch?", "1 Stern", "{0} Sterne",
            "Das tut uns leid. Was ist schiefgelaufen?", "Das liest nur das Restaurant.", "Super! Möchtest du noch etwas ergänzen?", "Deine Anmerkungen (optional)",
            "Wie erreichen wir dich? (optional)", "Telefon oder E-Mail, falls das Restaurant sich melden soll.", "Senden", "Wird gesendet…",
            "Danke. Das Restaurant liest es noch heute.", "Danke!", "Magst du es auf Google teilen? Das hilft sehr.", "Auf Google bewerten", "Schließen",
            "Danke für dein Feedback heute!", "Senden fehlgeschlagen. Bitte versuch es erneut."),
        ["fr"] = new("Comment s'est passée votre visite ?", "1 étoile", "{0} étoiles",
            "Désolés. Qu'est-ce qui n'a pas été ?", "Seul le restaurant le lit.", "Super ! Quelque chose à ajouter ?", "Vos commentaires (facultatif)",
            "Comment vous joindre ? (facultatif)", "Téléphone ou e-mail, si vous souhaitez que le restaurant vous réponde.", "Envoyer", "Envoi…",
            "Merci. Le restaurant le lira aujourd'hui.", "Merci !", "Voulez-vous le partager sur Google ? Cela aide beaucoup.", "Avis sur Google", "Fermer",
            "Merci pour votre avis aujourd'hui !", "Échec de l'envoi. Réessayez."),
        ["es"] = new("¿Qué tal tu visita?", "1 estrella", "{0} estrellas",
            "Lo sentimos. ¿Qué salió mal?", "Solo lo lee el restaurante.", "¡Genial! ¿Quieres añadir algo?", "Tus comentarios (opcional)",
            "¿Cómo podemos contactarte? (opcional)", "Teléfono o email, si quieres que el restaurante te responda.", "Enviar", "Enviando…",
            "Gracias. El restaurante lo leerá hoy.", "¡Gracias!", "¿Lo compartirías en Google? Ayuda mucho.", "Opinar en Google", "Cerrar",
            "¡Gracias por tu opinión de hoy!", "No se pudo enviar. Inténtalo de nuevo."),
        ["tr"] = new("Ziyaretiniz nasıldı?", "1 yıldız", "{0} yıldız",
            "Üzgünüz. Ne yolunda gitmedi?", "Bunu yalnızca restoran okur.", "Harika! Eklemek istediğiniz bir şey var mı?", "Yorumlarınız (isteğe bağlı)",
            "Size nasıl ulaşalım? (isteğe bağlı)", "Restoranın size dönmesini isterseniz telefon veya e-posta.", "Gönder", "Gönderiliyor…",
            "Teşekkürler. Restoran bunu bugün okuyacak.", "Teşekkürler!", "Google'da paylaşır mısınız? Çok yardımcı olur.", "Google'da değerlendir", "Kapat",
            "Bugünkü geri bildiriminiz için teşekkürler!", "Gönderilemedi. Lütfen tekrar deneyin.")
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];
}
