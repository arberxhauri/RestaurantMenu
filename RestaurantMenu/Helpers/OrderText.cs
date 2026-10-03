using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Guest-facing words for table ordering, in the menu's languages.</summary>
public static class OrderText
{
    public record Words(
        string Send,            // button
        string Sending,
        string Hint,            // above the list when ordering is on
        string NoteLabel,
        string NotePlaceholder,
        string Sent,            // confirmation title
        string OrderNumber,     // "Order #{0}"
        string YourOrders,
        string Received, string Preparing, string Served, string Cancelled,
        string Total,
        string AddMore,
        string Unavailable,     // ordering off / paused / table not valid
        string Closed,
        string NotNow,          // {0} = dish names
        string Changed,
        string TooBig,
        string Busy,
        string Network,
        string OldCode);        // menu opened with an outdated table code

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new("Send to kitchen", "Sending…", "Check your order, then send it to the kitchen.",
            "Note for the kitchen (optional)", "e.g. no onions, extra spicy",
            "Sent to the kitchen", "Order #{0}", "Your orders",
            "Received", "Being prepared", "Served", "Cancelled", "Total", "Add more dishes",
            "Ordering isn't available right now. Please ask your server.",
            "We're closed right now, so orders can't be sent.",
            "Can't be ordered right now: {0}. Remove them and send again.",
            "The menu just changed. Reload the page and check your list.",
            "That's a big order. Please send it in two parts, or ask your server.",
            "This table just sent several orders. Wait a minute and try again.",
            "Couldn't send. Check your connection and try again.",
            "This QR code can't send orders. Show your list to your server."),
        ["sq"] = new("Dërgoje në kuzhinë", "Po dërgohet…", "Kontrolloni porosinë, pastaj dërgojeni në kuzhinë.",
            "Shënim për kuzhinën (opsional)", "p.sh. pa qepë, më pikante",
            "U dërgua në kuzhinë", "Porosia #{0}", "Porositë tuaja",
            "U mor", "Po përgatitet", "U shërbye", "U anulua", "Totali", "Shto pjata të tjera",
            "Porositë nuk pranohen tani. Ju lutemi pyesni kamarierin.",
            "Jemi të mbyllur tani, ndaj porositë nuk mund të dërgohen.",
            "Nuk mund të porositen tani: {0}. Hiqini dhe dërgojeni përsëri.",
            "Menuja sapo ndryshoi. Rifreskoni faqen dhe kontrolloni listën.",
            "Porosi e madhe. Dërgojeni në dy pjesë ose pyesni kamarierin.",
            "Kjo tavolinë sapo dërgoi disa porosi. Prisni një minutë dhe provoni përsëri.",
            "Nuk u dërgua. Kontrolloni lidhjen dhe provoni përsëri.",
            "Ky kod QR nuk dërgon porosi. Tregojani listën kamarierit."),
        ["it"] = new("Invia in cucina", "Invio…", "Controlla l'ordine, poi invialo in cucina.",
            "Nota per la cucina (facoltativa)", "es. senza cipolla, più piccante",
            "Inviato in cucina", "Ordine n. {0}", "I tuoi ordini",
            "Ricevuto", "In preparazione", "Servito", "Annullato", "Totale", "Aggiungi altri piatti",
            "Al momento non si accettano ordini. Chiedi al cameriere.",
            "Ora siamo chiusi, quindi non è possibile inviare ordini.",
            "Non ordinabili ora: {0}. Rimuovili e invia di nuovo.",
            "Il menu è appena cambiato. Ricarica la pagina e controlla la lista.",
            "È un ordine grande. Invialo in due parti o chiedi al cameriere.",
            "Questo tavolo ha appena inviato vari ordini. Attendi un minuto e riprova.",
            "Invio non riuscito. Controlla la connessione e riprova.",
            "Questo codice QR non invia ordini. Mostra la lista al cameriere."),
        ["de"] = new("An die Küche senden", "Wird gesendet…", "Prüfe deine Bestellung und sende sie dann an die Küche.",
            "Hinweis für die Küche (optional)", "z. B. ohne Zwiebeln, extra scharf",
            "An die Küche gesendet", "Bestellung Nr. {0}", "Deine Bestellungen",
            "Eingegangen", "Wird zubereitet", "Serviert", "Storniert", "Summe", "Weitere Gerichte hinzufügen",
            "Bestellungen sind gerade nicht möglich. Bitte frag das Personal.",
            "Wir haben gerade geschlossen, daher können keine Bestellungen gesendet werden.",
            "Gerade nicht bestellbar: {0}. Entferne sie und sende erneut.",
            "Die Karte hat sich gerade geändert. Lade die Seite neu und prüfe deine Liste.",
            "Das ist eine große Bestellung. Sende sie in zwei Teilen oder frag das Personal.",
            "Dieser Tisch hat gerade mehrere Bestellungen gesendet. Warte eine Minute und versuch es erneut.",
            "Senden fehlgeschlagen. Prüfe deine Verbindung und versuch es erneut.",
            "Mit diesem QR-Code kann nicht bestellt werden. Zeig deine Liste dem Personal."),
        ["fr"] = new("Envoyer en cuisine", "Envoi…", "Vérifiez votre commande, puis envoyez-la en cuisine.",
            "Note pour la cuisine (facultatif)", "ex. sans oignons, plus épicé",
            "Envoyée en cuisine", "Commande n° {0}", "Vos commandes",
            "Reçue", "En préparation", "Servie", "Annulée", "Total", "Ajouter d'autres plats",
            "Les commandes ne sont pas disponibles pour le moment. Demandez au serveur.",
            "Nous sommes fermés, les commandes ne peuvent pas être envoyées.",
            "Impossible à commander maintenant : {0}. Retirez-les et renvoyez.",
            "Le menu vient de changer. Rechargez la page et vérifiez votre liste.",
            "C'est une grosse commande. Envoyez-la en deux fois ou demandez au serveur.",
            "Cette table vient d'envoyer plusieurs commandes. Patientez une minute et réessayez.",
            "Échec de l'envoi. Vérifiez votre connexion et réessayez.",
            "Ce code QR ne permet pas de commander. Montrez votre liste au serveur."),
        ["es"] = new("Enviar a cocina", "Enviando…", "Revisa tu pedido y luego envíalo a cocina.",
            "Nota para cocina (opcional)", "p. ej. sin cebolla, más picante",
            "Enviado a cocina", "Pedido n.º {0}", "Tus pedidos",
            "Recibido", "En preparación", "Servido", "Cancelado", "Total", "Añadir más platos",
            "Ahora no se aceptan pedidos. Pregunta al camarero.",
            "Ahora estamos cerrados, así que no se pueden enviar pedidos.",
            "No se puede pedir ahora: {0}. Quítalos y vuelve a enviar.",
            "El menú acaba de cambiar. Recarga la página y revisa tu lista.",
            "Es un pedido grande. Envíalo en dos partes o pregunta al camarero.",
            "Esta mesa acaba de enviar varios pedidos. Espera un minuto y vuelve a intentarlo.",
            "No se pudo enviar. Revisa tu conexión y vuelve a intentarlo.",
            "Este código QR no permite pedir. Muestra tu lista al camarero."),
        ["tr"] = new("Mutfağa gönder", "Gönderiliyor…", "Siparişinizi kontrol edin, sonra mutfağa gönderin.",
            "Mutfak için not (isteğe bağlı)", "örn. soğansız, ekstra acı",
            "Mutfağa gönderildi", "Sipariş #{0}", "Siparişleriniz",
            "Alındı", "Hazırlanıyor", "Servis edildi", "İptal edildi", "Toplam", "Başka yemek ekle",
            "Şu anda sipariş alınmıyor. Lütfen garsona sorun.",
            "Şu anda kapalıyız, bu yüzden sipariş gönderilemiyor.",
            "Şu anda sipariş edilemiyor: {0}. Bunları çıkarıp tekrar gönderin.",
            "Menü az önce değişti. Sayfayı yenileyip listenizi kontrol edin.",
            "Bu büyük bir sipariş. Lütfen iki parça halinde gönderin veya garsona sorun.",
            "Bu masa az önce birkaç sipariş gönderdi. Bir dakika bekleyip tekrar deneyin.",
            "Gönderilemedi. Bağlantınızı kontrol edip tekrar deneyin.",
            "Bu QR kod ile sipariş verilemez. Listenizi garsona gösterin.")
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];

    public static string Status(OrderStatus status, string? language)
    {
        var w = For(language);
        return status switch
        {
            OrderStatus.Preparing => w.Preparing,
            OrderStatus.Served => w.Served,
            OrderStatus.Cancelled => w.Cancelled,
            _ => w.Received
        };
    }

    public static string Problem(OrderProblem problem, string? language) => problem switch
    {
        OrderProblem.Closed => For(language).Closed,
        OrderProblem.MenuChanged or OrderProblem.Invalid => For(language).Changed,
        OrderProblem.TooBig => For(language).TooBig,
        OrderProblem.Busy => For(language).Busy,
        OrderProblem.Empty => For(language).Changed,
        _ => For(language).Unavailable
    };
}
