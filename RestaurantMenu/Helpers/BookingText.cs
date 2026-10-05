using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>Guest-facing words for bookings (page, confirmation, SMS and email), in the menu's languages.</summary>
public static class BookingText
{
    public record Words(
        string Title,           // "Book a table"
        string Guests, string OneGuest, string ManyGuests, // "{0} guests"
        string Date, string Today, string Tomorrow, string Time,
        string NoSlots, string ClosedDay, string Loading,
        string Details, string Name, string Phone, string PhoneHint, string Email, string EmailHint,
        string Note, string NotePlaceholder,
        string Book,            // button
        string Booking,         // "Booking…"
        string LargeGroup,      // {0} = max party, {1} = phone
        string SlotTaken, string NameRequired, string PhoneInvalid, string EmailInvalid, string TooMany,
        string Unavailable,     // {0} = phone
        string Network,
        string ConfirmedTitle, string PendingTitle, string PendingText, string CancelledTitle, string CancelledText, string PastText,
        string When, string AddToCalendar, string Cancel, string CancelConfirm, string BookAgain, string SeeMenu,
        string StatusPending, string StatusConfirmed, string StatusSeated, string StatusCancelled, string StatusNoShow,
        // Messages: {0} = branch, {1} = "4 guests", {2} = date, {3} = time, {4} = link
        string MsgConfirmed, string MsgPending, string MsgCancelled, string MsgReminder,
        string EmailSubject);   // {0} = branch

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new("Book a table", "Guests", "1 guest", "{0} guests", "Date", "Today", "Tomorrow", "Time",
            "No free times on this day. Try another day.", "We don't take bookings on this day.", "Finding free times…",
            "Your details", "Name", "Phone", "We'll only contact you about this booking.", "Email (optional)", "For your confirmation.",
            "Requests (optional)", "e.g. high chair, birthday, terrace",
            "Book table", "Booking…",
            "For more than {0} guests, please call us: {1}",
            "Sorry, that time was just taken. Please pick another.", "Enter your name.", "Enter a phone number we can reach you on, e.g. 069 123 4567.",
            "That email doesn't look right.", "This number already has several upcoming bookings here. Please call us to book more.",
            "Online booking isn't available right now. Please call us: {0}",
            "Couldn't book. Check your connection and try again.",
            "You're booked!", "Request received", "The restaurant will confirm shortly. You'll get a message.", "Booking cancelled",
            "Your table is released. Hope to see you another time.", "This booking has passed.",
            "When", "Add to calendar", "Cancel booking", "Cancel this booking?", "Make another booking", "See the menu",
            "Waiting for confirmation", "Confirmed", "Seated", "Cancelled", "Missed",
            "{0}: your table for {1} on {2} at {3} is confirmed. Details or cancel: {4}",
            "{0}: we got your request for {1} on {2} at {3} and will confirm soon. {4}",
            "{0}: your booking for {1} on {2} at {3} was cancelled. {4}",
            "{0}: see you today at {3}, table for {1}. Can't make it? Cancel here: {4}",
            "Your table at {0}"),
        ["sq"] = new("Rezervo një tavolinë", "Persona", "1 person", "{0} persona", "Data", "Sot", "Nesër", "Ora",
            "Nuk ka orare të lira këtë ditë. Provoni një ditë tjetër.", "Nuk pranojmë rezervime këtë ditë.", "Po kërkojmë orare të lira…",
            "Të dhënat tuaja", "Emri", "Telefoni", "Do t'ju kontaktojmë vetëm për këtë rezervim.", "Email (opsional)", "Për konfirmimin tuaj.",
            "Kërkesa (opsionale)", "p.sh. karrige për fëmijë, ditëlindje, tarracë",
            "Rezervo", "Po rezervohet…",
            "Për më shumë se {0} persona, ju lutemi na telefononi: {1}",
            "Na vjen keq, ky orar sapo u zu. Zgjidhni një tjetër.", "Shkruani emrin tuaj.", "Shkruani një numër telefoni, p.sh. 069 123 4567.",
            "Ky email nuk duket i saktë.", "Ky numër ka tashmë disa rezervime të ardhshme këtu. Na telefononi për më shumë.",
            "Rezervimi online nuk është i disponueshëm tani. Na telefononi: {0}",
            "Rezervimi nuk u krye. Kontrolloni lidhjen dhe provoni përsëri.",
            "Rezervimi u bë!", "Kërkesa u mor", "Restoranti do ta konfirmojë së shpejti. Do të merrni një mesazh.", "Rezervimi u anulua",
            "Tavolina juaj u lirua. Shpresojmë t'ju shohim një herë tjetër.", "Ky rezervim ka kaluar.",
            "Kur", "Shto në kalendar", "Anulo rezervimin", "Ta anulojmë këtë rezervim?", "Bëj një rezervim tjetër", "Shiko menunë",
            "Në pritje të konfirmimit", "I konfirmuar", "Të ulur", "I anuluar", "Nuk erdhën",
            "{0}: tavolina juaj për {1} më {2} në orën {3} është konfirmuar. Detaje ose anulim: {4}",
            "{0}: morëm kërkesën tuaj për {1} më {2} në orën {3} dhe do ta konfirmojmë së shpejti. {4}",
            "{0}: rezervimi juaj për {1} më {2} në orën {3} u anulua. {4}",
            "{0}: ju presim sot në orën {3}, tavolinë për {1}. Nuk vini dot? Anuloni këtu: {4}",
            "Tavolina juaj te {0}"),
        ["it"] = new("Prenota un tavolo", "Persone", "1 persona", "{0} persone", "Data", "Oggi", "Domani", "Ora",
            "Nessun orario libero in questo giorno. Prova un altro giorno.", "In questo giorno non accettiamo prenotazioni.", "Cerco orari liberi…",
            "I tuoi dati", "Nome", "Telefono", "Ti contatteremo solo per questa prenotazione.", "Email (facoltativa)", "Per la conferma.",
            "Richieste (facoltative)", "es. seggiolone, compleanno, terrazza",
            "Prenota", "Prenotazione…",
            "Per più di {0} persone, chiamaci: {1}",
            "Peccato, quell'orario è appena stato preso. Scegline un altro.", "Inserisci il tuo nome.", "Inserisci un numero di telefono, es. +39 333 123 4567.",
            "Questa email non sembra corretta.", "Questo numero ha già varie prenotazioni future qui. Chiamaci per prenotare ancora.",
            "La prenotazione online non è disponibile ora. Chiamaci: {0}",
            "Prenotazione non riuscita. Controlla la connessione e riprova.",
            "Prenotato!", "Richiesta ricevuta", "Il ristorante confermerà a breve. Riceverai un messaggio.", "Prenotazione annullata",
            "Il tavolo è stato liberato. Speriamo di vederti un'altra volta.", "Questa prenotazione è passata.",
            "Quando", "Aggiungi al calendario", "Annulla prenotazione", "Annullare questa prenotazione?", "Nuova prenotazione", "Vedi il menu",
            "In attesa di conferma", "Confermata", "Al tavolo", "Annullata", "Non presentati",
            "{0}: il tuo tavolo per {1} il {2} alle {3} è confermato. Dettagli o annullamento: {4}",
            "{0}: abbiamo ricevuto la tua richiesta per {1} il {2} alle {3}, la confermeremo a breve. {4}",
            "{0}: la tua prenotazione per {1} il {2} alle {3} è stata annullata. {4}",
            "{0}: ti aspettiamo oggi alle {3}, tavolo per {1}. Non riesci a venire? Annulla qui: {4}",
            "Il tuo tavolo da {0}"),
        ["de"] = new("Tisch reservieren", "Personen", "1 Person", "{0} Personen", "Datum", "Heute", "Morgen", "Uhrzeit",
            "An diesem Tag ist nichts mehr frei. Versuch einen anderen Tag.", "An diesem Tag nehmen wir keine Reservierungen an.", "Suche freie Zeiten…",
            "Deine Angaben", "Name", "Telefon", "Wir melden uns nur wegen dieser Reservierung.", "E-Mail (optional)", "Für deine Bestätigung.",
            "Wünsche (optional)", "z. B. Kinderstuhl, Geburtstag, Terrasse",
            "Reservieren", "Wird reserviert…",
            "Für mehr als {0} Personen ruf uns bitte an: {1}",
            "Diese Zeit wurde gerade vergeben. Bitte wähle eine andere.", "Gib deinen Namen ein.", "Gib eine Telefonnummer ein, z. B. +49 151 1234567.",
            "Diese E-Mail-Adresse sieht nicht richtig aus.", "Für diese Nummer gibt es hier schon mehrere Reservierungen. Ruf uns für weitere an.",
            "Online-Reservierung ist gerade nicht möglich. Ruf uns an: {0}",
            "Reservierung fehlgeschlagen. Prüfe deine Verbindung und versuch es erneut.",
            "Reserviert!", "Anfrage erhalten", "Das Restaurant bestätigt in Kürze. Du bekommst eine Nachricht.", "Reservierung storniert",
            "Dein Tisch ist wieder frei. Bis zum nächsten Mal!", "Diese Reservierung liegt in der Vergangenheit.",
            "Wann", "Zum Kalender hinzufügen", "Reservierung stornieren", "Diese Reservierung stornieren?", "Neue Reservierung", "Zur Speisekarte",
            "Wartet auf Bestätigung", "Bestätigt", "Am Tisch", "Storniert", "Nicht erschienen",
            "{0}: dein Tisch für {1} am {2} um {3} ist bestätigt. Details oder Storno: {4}",
            "{0}: wir haben deine Anfrage für {1} am {2} um {3} erhalten und bestätigen bald. {4}",
            "{0}: deine Reservierung für {1} am {2} um {3} wurde storniert. {4}",
            "{0}: bis heute um {3}, Tisch für {1}. Du schaffst es nicht? Hier stornieren: {4}",
            "Dein Tisch im {0}"),
        ["fr"] = new("Réserver une table", "Personnes", "1 personne", "{0} personnes", "Date", "Aujourd'hui", "Demain", "Heure",
            "Aucun horaire libre ce jour-là. Essayez un autre jour.", "Nous ne prenons pas de réservations ce jour-là.", "Recherche des horaires libres…",
            "Vos coordonnées", "Nom", "Téléphone", "Nous vous contacterons uniquement pour cette réservation.", "E-mail (facultatif)", "Pour votre confirmation.",
            "Demandes (facultatif)", "ex. chaise haute, anniversaire, terrasse",
            "Réserver", "Réservation…",
            "Au-delà de {0} personnes, appelez-nous : {1}",
            "Désolé, cet horaire vient d'être pris. Choisissez-en un autre.", "Indiquez votre nom.", "Indiquez un numéro de téléphone, ex. +33 6 12 34 56 78.",
            "Cet e-mail ne semble pas correct.", "Ce numéro a déjà plusieurs réservations à venir ici. Appelez-nous pour en ajouter.",
            "La réservation en ligne n'est pas disponible. Appelez-nous : {0}",
            "Échec de la réservation. Vérifiez votre connexion et réessayez.",
            "C'est réservé !", "Demande reçue", "Le restaurant confirmera bientôt. Vous recevrez un message.", "Réservation annulée",
            "Votre table est libérée. À une prochaine fois !", "Cette réservation est passée.",
            "Quand", "Ajouter au calendrier", "Annuler la réservation", "Annuler cette réservation ?", "Nouvelle réservation", "Voir le menu",
            "En attente de confirmation", "Confirmée", "À table", "Annulée", "Absents",
            "{0} : votre table pour {1} le {2} à {3} est confirmée. Détails ou annulation : {4}",
            "{0} : nous avons reçu votre demande pour {1} le {2} à {3} et la confirmerons bientôt. {4}",
            "{0} : votre réservation pour {1} le {2} à {3} a été annulée. {4}",
            "{0} : à aujourd'hui {3}, table pour {1}. Empêché ? Annulez ici : {4}",
            "Votre table chez {0}"),
        ["es"] = new("Reservar mesa", "Personas", "1 persona", "{0} personas", "Fecha", "Hoy", "Mañana", "Hora",
            "No hay horas libres este día. Prueba otro día.", "Este día no aceptamos reservas.", "Buscando horas libres…",
            "Tus datos", "Nombre", "Teléfono", "Solo te contactaremos por esta reserva.", "Email (opcional)", "Para tu confirmación.",
            "Peticiones (opcional)", "p. ej. trona, cumpleaños, terraza",
            "Reservar", "Reservando…",
            "Para más de {0} personas, llámanos: {1}",
            "Lo sentimos, esa hora se acaba de ocupar. Elige otra.", "Escribe tu nombre.", "Escribe un teléfono, p. ej. +34 612 345 678.",
            "Ese email no parece correcto.", "Este número ya tiene varias reservas próximas aquí. Llámanos para hacer más.",
            "La reserva online no está disponible ahora. Llámanos: {0}",
            "No se pudo reservar. Revisa tu conexión y vuelve a intentarlo.",
            "¡Reservado!", "Solicitud recibida", "El restaurante confirmará pronto. Recibirás un mensaje.", "Reserva cancelada",
            "Tu mesa ha quedado libre. ¡Esperamos verte otra vez!", "Esta reserva ya pasó.",
            "Cuándo", "Añadir al calendario", "Cancelar reserva", "¿Cancelar esta reserva?", "Hacer otra reserva", "Ver el menú",
            "Pendiente de confirmación", "Confirmada", "En la mesa", "Cancelada", "No se presentaron",
            "{0}: tu mesa para {1} el {2} a las {3} está confirmada. Detalles o cancelar: {4}",
            "{0}: hemos recibido tu solicitud para {1} el {2} a las {3} y la confirmaremos pronto. {4}",
            "{0}: tu reserva para {1} el {2} a las {3} se ha cancelado. {4}",
            "{0}: te esperamos hoy a las {3}, mesa para {1}. ¿No puedes venir? Cancela aquí: {4}",
            "Tu mesa en {0}"),
        ["tr"] = new("Masa ayırt", "Kişi", "1 kişi", "{0} kişi", "Tarih", "Bugün", "Yarın", "Saat",
            "Bu gün için boş saat yok. Başka bir gün deneyin.", "Bu gün rezervasyon almıyoruz.", "Boş saatler aranıyor…",
            "Bilgileriniz", "Ad", "Telefon", "Sizinle yalnızca bu rezervasyon için iletişime geçeceğiz.", "E-posta (isteğe bağlı)", "Onayınız için.",
            "İstekler (isteğe bağlı)", "örn. mama sandalyesi, doğum günü, teras",
            "Rezervasyon yap", "Rezervasyon yapılıyor…",
            "{0} kişiden fazlası için lütfen bizi arayın: {1}",
            "Üzgünüz, bu saat az önce doldu. Lütfen başka bir saat seçin.", "Adınızı yazın.", "Bir telefon numarası yazın, örn. +90 532 123 4567.",
            "Bu e-posta doğru görünmüyor.", "Bu numaranın burada zaten birkaç yaklaşan rezervasyonu var. Daha fazlası için bizi arayın.",
            "Çevrim içi rezervasyon şu anda kullanılamıyor. Bizi arayın: {0}",
            "Rezervasyon yapılamadı. Bağlantınızı kontrol edip tekrar deneyin.",
            "Rezervasyonunuz tamam!", "Talep alındı", "Restoran kısa süre içinde onaylayacak. Size mesaj gelecek.", "Rezervasyon iptal edildi",
            "Masanız serbest bırakıldı. Başka bir zaman görüşmek üzere.", "Bu rezervasyonun tarihi geçti.",
            "Ne zaman", "Takvime ekle", "Rezervasyonu iptal et", "Bu rezervasyon iptal edilsin mi?", "Yeni rezervasyon yap", "Menüyü gör",
            "Onay bekleniyor", "Onaylandı", "Masada", "İptal edildi", "Gelmedi",
            "{0}: {2} {3} için {1} masanız onaylandı. Ayrıntılar veya iptal: {4}",
            "{0}: {2} {3} için {1} talebinizi aldık, kısa sürede onaylayacağız. {4}",
            "{0}: {2} {3} için {1} rezervasyonunuz iptal edildi. {4}",
            "{0}: bugün {3} saatinde görüşmek üzere, {1} masa. Gelemiyor musunuz? Buradan iptal edin: {4}",
            "{0} rezervasyonunuz")
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];

    // Shown under the booking form, where the guest's details are collected, with a link to
    // the privacy page. {0} = branch. Kept apart from Words so it doesn't renumber that record.
    private static readonly Dictionary<string, (string Text, string Link)> PrivacyNotes = new()
    {
        ["en"] = ("{0} uses your details only for this booking.", "Privacy"),
        ["sq"] = ("{0} i përdor të dhënat tuaja vetëm për këtë rezervim.", "Privatësia"),
        ["it"] = ("{0} usa i tuoi dati solo per questa prenotazione.", "Privacy"),
        ["de"] = ("{0} nutzt deine Angaben nur für diese Reservierung.", "Datenschutz"),
        ["fr"] = ("{0} utilise vos coordonnées uniquement pour cette réservation.", "Confidentialité"),
        ["es"] = ("{0} usa tus datos solo para esta reserva.", "Privacidad"),
        ["tr"] = ("{0} bilgilerinizi yalnızca bu rezervasyon için kullanır.", "Gizlilik")
    };

    public static (string Text, string Link) PrivacyNote(string branch, string? language)
    {
        var n = PrivacyNotes.TryGetValue(language ?? "en", out var x) ? x : PrivacyNotes["en"];
        return (string.Format(n.Text, branch), n.Link);
    }

    public static string GuestCount(int n, string? language) =>
        n == 1 ? For(language).OneGuest : string.Format(For(language).ManyGuests, n);

    public static CultureInfo Culture(string? language)
    {
        try { return CultureInfo.GetCultureInfo(language ?? "en"); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
    }

    /// <summary>"Friday 10 October" in the guest's language.</summary>
    public static string LongDate(DateTime local, string? language) =>
        local.ToString("dddd d MMMM", Culture(language));

    /// <summary>"Fri 10" for the date chips.</summary>
    public static (string Day, string Number) DateChip(DateOnly d, string? language) =>
        (d.ToDateTime(TimeOnly.MinValue).ToString("ddd", Culture(language)).TrimEnd('.'), d.Day.ToString(CultureInfo.InvariantCulture));

    public static string Status(ReservationStatus s, string? language)
    {
        var w = For(language);
        return s switch
        {
            ReservationStatus.Pending => w.StatusPending,
            ReservationStatus.Seated => w.StatusSeated,
            ReservationStatus.Cancelled => w.StatusCancelled,
            ReservationStatus.NoShow => w.StatusNoShow,
            _ => w.StatusConfirmed
        };
    }

    /// <summary>The SMS / email line for a booking: confirmed, pending, cancelled or reminder.</summary>
    public static string Message(string template, string branch, Reservation r, string link) =>
        string.Format(template, branch, GuestCount(r.Guests, r.Language), LongDate(r.StartsAtLocal, r.Language),
            r.StartsAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture), link);
}
