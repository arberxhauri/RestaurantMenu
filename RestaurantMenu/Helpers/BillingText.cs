using System.Globalization;
using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// The owner's billing words (the /billing page, invoice PDFs and billing emails) in English and
/// Albanian, the owner's language from signup. Checked by TranslationCoverageTests.
/// </summary>
public static class BillingText
{
    public static readonly string[] Languages = { "en", "sq" };

    public record Words(
        // Page
        string Title, string Intro, string YourPlan, string Branches, string Monthly, string Yearly,
        string PerMonth, string PerYear, string PriceOnRequest, string VatExtra,
        string StatusTrial, string StatusActive, string StatusActiveNoEnd, string StatusPastDue, string StatusReadOnly,
        string StatusTrialEnded, string StatusCancelled, string StatusLegacy, string StatusCancelling, string NextChange,
        // Change
        string ChangeTitle, string ChangeHintTrial, string ChangeHintPaid, string SaveChange, string ChangeSavedNow, string ChangeSavedNext, string UndoChange, string ChangeUndone,
        // Pay
        string PayTitle, string PayIntro, string LegalName, string Nipt, string NiptHint, string Address, string City, string BillingEmail,
        string GetInvoice, string ErrLegalName, string ErrNipt, string ErrEmail, string ErrPrice, string ErrNotReady, string ErrCancelled, string InvoiceCreated,
        // Open invoice
        string OpenInvoice, string AmountDue, string DueBy, string Beneficiary, string Bank, string Iban, string Swift, string Reference, string ReferenceHint,
        string DownloadPdf, string Overdue,
        // List
        string InvoicesTitle, string NoInvoices, string ColNumber, string ColPeriod, string ColAmount, string ColStatus,
        string InvOpen, string InvPaid, string InvVoid, string InvOverdue,
        // Cancel
        string CancelTitle, string CancelText, string CancelButton, string CancelDone, string ResumeText, string ResumeButton, string Resumed,
        // PDF
        string PdfInvoice, string PdfFrom, string PdfTo, string PdfDate, string PdfDue, string PdfPeriod, string PdfItem, string PdfQty,
        string PdfUnit, string PdfAmount, string PdfSubtotal, string PdfVat, string PdfNoVat, string PdfTotal, string PdfPayTo, string PdfPaid, string PdfPerBranch,
        // Emails
        string EmailButton, string InvoiceSubject, string InvoiceText, string DueSoonSubject, string DueSoonText,
        string PaidSubject, string PaidText, string TrialSubject, string TrialSubjectOne, string TrialText,
        string TrialEndedSubject, string TrialEndedText, string PastDueSubject, string PastDueText,
        string GraceSubject, string GraceText, string ReadOnlySubject, string ReadOnlyText, string CancelledSubject, string CancelledText,
        // Card (Paddle)
        string PayCardTitle, string PayCardIntro, string PayCardButton, string CardNote, string ManageCard, string ChangeHintCard,
        string ChangeSavedCard, string ErrCard, string CardThanks, string PayPageTitle, string PayPageText, string PayPageBack, string PastDueCardText, string PaidByCard);

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new Words(
            Title: "Billing", Intro: "Your plan, how you pay, and your invoices.",
            YourPlan: "Your plan", Branches: "Branches", Monthly: "Monthly", Yearly: "Yearly",
            PerMonth: "{0} a month", PerYear: "{0} a year", PriceOnRequest: "Price on request", VatExtra: "plus VAT",
            StatusTrial: "Free trial: {0} days left, until {1}.", StatusActive: "Active, paid until {0}.", StatusActiveNoEnd: "Active.",
            StatusPastDue: "Payment overdue. Everything keeps working until {0}; then the back office becomes read-only.",
            StatusReadOnly: "Read-only: pay the invoice below to switch everything back on. Your menus are online.",
            StatusTrialEnded: "Your free trial has ended. Choose how to pay to switch everything back on. Your menus are online.",
            StatusCancelled: "Your plan has ended. Your menus are online; get a new invoice to continue.",
            StatusLegacy: "Legacy plan: everything included, no charge.",
            StatusCancelling: "Cancelled: it ends on {0} and won't renew.",
            NextChange: "From {0}: {1}.",
            ChangeTitle: "Change your plan",
            ChangeHintTrial: "During the trial changes apply straight away.",
            ChangeHintPaid: "Changes start with your next period ({0}); the next invoice will include them.",
            SaveChange: "Save changes", ChangeSavedNow: "Saved. Your plan now includes these modules.",
            ChangeSavedNext: "Saved. These apply from {0}.", UndoChange: "Keep my current plan", ChangeUndone: "Your current plan stays as it is.",
            PayTitle: "Pay by bank transfer",
            PayIntro: "We'll send you an invoice for the period starting {0}. Pay it by bank transfer within {1} days, with the reference on it.",
            LegalName: "Business name (as on invoices)", Nipt: "NIPT", NiptHint: "Required for businesses in Albania.",
            Address: "Address", City: "City", BillingEmail: "Send invoices to",
            GetInvoice: "Get the invoice",
            ErrLegalName: "Enter your business name as it should appear on invoices.",
            ErrNipt: "Enter your NIPT, e.g. L21507023K.", ErrEmail: "Enter a valid email address for invoices.",
            ErrPrice: "Some modules in your plan have no price yet. Contact us and we'll set it up.",
            ErrNotReady: "Paying by bank transfer isn't set up yet. Contact us and we'll send you an invoice.",
            ErrCancelled: "There's already an invoice to pay for this period (below).",
            InvoiceCreated: "Invoice {0} is ready. We've emailed it to {1}.",
            OpenInvoice: "Invoice {0}", AmountDue: "Amount due", DueBy: "Pay by {0}", Beneficiary: "Beneficiary", Bank: "Bank",
            Iban: "IBAN", Swift: "SWIFT/BIC", Reference: "Reference", ReferenceHint: "Write it exactly like this so we can match your payment.",
            DownloadPdf: "Download the PDF", Overdue: "Overdue",
            InvoicesTitle: "Invoices", NoInvoices: "No invoices yet.",
            ColNumber: "Number", ColPeriod: "Period", ColAmount: "Amount", ColStatus: "Status",
            InvOpen: "To pay", InvPaid: "Paid", InvVoid: "Cancelled", InvOverdue: "Overdue",
            CancelTitle: "Cancel your plan",
            CancelText: "It stops renewing. Everything keeps working until {0}; then the back office becomes read-only. Your menus stay online.",
            CancelButton: "Cancel at the end of the period", CancelDone: "Cancelled. Everything keeps working until {0}.",
            ResumeText: "Your plan ends on {0}.", ResumeButton: "Keep my plan", Resumed: "Your plan renews as usual.",
            PdfInvoice: "Invoice", PdfFrom: "From", PdfTo: "Bill to", PdfDate: "Date", PdfDue: "Due", PdfPeriod: "Period",
            PdfItem: "Item", PdfQty: "Qty", PdfUnit: "Unit price", PdfAmount: "Amount", PdfSubtotal: "Subtotal", PdfVat: "VAT {0}%",
            PdfNoVat: "No VAT charged.", PdfTotal: "Total", PdfPayTo: "Pay by bank transfer to", PdfPaid: "Paid on {0}", PdfPerBranch: "per branch",
            EmailButton: "Open billing",
            InvoiceSubject: "Invoice {0} from My Quick Menu",
            InvoiceText: "Your invoice {0} is attached: {1}, due by {2}. Pay by bank transfer to IBAN {3} with the reference {0}.",
            DueSoonSubject: "Invoice {0} is due on {1}",
            DueSoonText: "A reminder: invoice {0} for {1} is due on {2}. If you've already paid, thank you, and ignore this email.",
            PaidSubject: "Payment received, thank you",
            PaidText: "We received payment for invoice {0} ({1}). Your plan is active until {2}.",
            TrialSubject: "Your free trial ends in {0} days", TrialSubjectOne: "Your free trial ends tomorrow",
            TrialText: "Your trial ends on {0}. Choose how to pay to keep everything running; your menus stay online either way.",
            TrialEndedSubject: "Your free trial has ended",
            TrialEndedText: "Your menus are still online, but the back office is now read-only and paid features are paused. Choose how to pay to switch them back on.",
            PastDueSubject: "Your payment is overdue",
            PastDueText: "We haven't received payment for invoice {0}. Everything keeps working until {1}; after that the back office becomes read-only. Your menus stay online.",
            GraceSubject: "Your account becomes read-only on {0}",
            GraceText: "Invoice {0} is still unpaid. On {1} the back office becomes read-only and paid features pause. Your menus stay online.",
            ReadOnlySubject: "Your account is now read-only",
            ReadOnlyText: "The back office is read-only and paid features are paused. Pay the open invoice to switch everything back on. Your menus are still online.",
            CancelledSubject: "Your plan has ended",
            CancelledText: "Your plan has ended as you asked. Your menus are still online; the back office is read-only. Get a new invoice whenever you want to continue.",
            PayCardTitle: "Pay by card", PayCardIntro: "Charged today and then every period automatically. Your paid period starts today.",
            PayCardButton: "Pay by card",
            CardNote: "Card payments are processed by Paddle, our reseller: Paddle charges your card, adds VAT where it applies and sends the receipt.",
            ManageCard: "Change card or download receipts",
            ChangeHintCard: "Changes apply straight away; the difference for the rest of this period is charged or credited to your card.",
            ChangeSavedCard: "Saved. Your plan is updated; the difference for this period is charged or credited.",
            ErrCard: "Card payment couldn't be started just now. Try again in a minute, or pay by bank transfer.",
            CardThanks: "Thank you! Your payment is being confirmed; this page updates within a minute.",
            PayPageTitle: "Pay by card", PayPageText: "The secure payment form opens here. If it doesn't, allow pop-ups and reload the page.",
            PayPageBack: "Back to billing",
            PastDueCardText: "Your card payment didn't go through. We'll try again over the next days; you can also change the card. Everything keeps working until {0}; after that the back office becomes read-only. Your menus stay online.",
            PaidByCard: "Paid by card"),

        ["sq"] = new Words(
            Title: "Faturimi", Intro: "Plani juaj, mënyra e pagesës dhe faturat.",
            YourPlan: "Plani juaj", Branches: "Pikat", Monthly: "Mujore", Yearly: "Vjetore",
            PerMonth: "{0} në muaj", PerYear: "{0} në vit", PriceOnRequest: "Çmimi me kërkesë", VatExtra: "plus TVSH",
            StatusTrial: "Provë falas: kanë mbetur {0} ditë, deri më {1}.", StatusActive: "Aktiv, i paguar deri më {0}.", StatusActiveNoEnd: "Aktiv.",
            StatusPastDue: "Pagesa është vonuar. Gjithçka funksionon deri më {0}; pastaj paneli kalon vetëm për lexim.",
            StatusReadOnly: "Vetëm për lexim: paguani faturën më poshtë që gjithçka të rikthehet. Menutë janë online.",
            StatusTrialEnded: "Prova juaj falas mbaroi. Zgjidhni si të paguani që gjithçka të rikthehet. Menutë janë online.",
            StatusCancelled: "Plani juaj mbaroi. Menutë janë online; merrni një faturë të re për të vazhduar.",
            StatusLegacy: "Plan i vjetër: gjithçka e përfshirë, pa pagesë.",
            StatusCancelling: "I anuluar: mbaron më {0} dhe nuk rinovohet.",
            NextChange: "Nga {0}: {1}.",
            ChangeTitle: "Ndryshoni planin",
            ChangeHintTrial: "Gjatë provës ndryshimet vlejnë menjëherë.",
            ChangeHintPaid: "Ndryshimet fillojnë me periudhën e ardhshme ({0}); fatura tjetër do t'i përfshijë.",
            SaveChange: "Ruaj ndryshimet", ChangeSavedNow: "U ruajt. Plani juaj tani përfshin këto module.",
            ChangeSavedNext: "U ruajt. Këto vlejnë nga {0}.", UndoChange: "Mbaj planin aktual", ChangeUndone: "Plani juaj aktual mbetet siç është.",
            PayTitle: "Pagesë me transfertë bankare",
            PayIntro: "Do t'ju dërgojmë një faturë për periudhën që fillon më {0}. Paguajeni me transfertë bankare brenda {1} ditëve, me referencën që ka.",
            LegalName: "Emri i biznesit (si në fatura)", Nipt: "NIPT", NiptHint: "I detyrueshëm për bizneset në Shqipëri.",
            Address: "Adresa", City: "Qyteti", BillingEmail: "Dërgo faturat te",
            GetInvoice: "Merr faturën",
            ErrLegalName: "Shkruani emrin e biznesit siç duhet të dalë në fatura.",
            ErrNipt: "Shkruani NIPT-in, p.sh. L21507023K.", ErrEmail: "Shkruani një email të vlefshëm për faturat.",
            ErrPrice: "Disa module në planin tuaj nuk kanë ende çmim. Na kontaktoni dhe do ta rregullojmë.",
            ErrNotReady: "Pagesa me transfertë bankare nuk është gati ende. Na kontaktoni dhe do t'ju dërgojmë faturën.",
            ErrCancelled: "Ka tashmë një faturë për t'u paguar për këtë periudhë (më poshtë).",
            InvoiceCreated: "Fatura {0} është gati. Jua dërguam me email te {1}.",
            OpenInvoice: "Fatura {0}", AmountDue: "Shuma për t'u paguar", DueBy: "Paguani deri më {0}", Beneficiary: "Përfituesi", Bank: "Banka",
            Iban: "IBAN", Swift: "SWIFT/BIC", Reference: "Referenca", ReferenceHint: "Shkruajeni saktësisht kështu që ta gjejmë pagesën tuaj.",
            DownloadPdf: "Shkarko PDF-në", Overdue: "E vonuar",
            InvoicesTitle: "Faturat", NoInvoices: "Ende nuk ka fatura.",
            ColNumber: "Numri", ColPeriod: "Periudha", ColAmount: "Shuma", ColStatus: "Gjendja",
            InvOpen: "Për t'u paguar", InvPaid: "E paguar", InvVoid: "E anuluar", InvOverdue: "E vonuar",
            CancelTitle: "Anuloni planin",
            CancelText: "Nuk rinovohet më. Gjithçka funksionon deri më {0}; pastaj paneli kalon vetëm për lexim. Menutë mbeten online.",
            CancelButton: "Anuloje në fund të periudhës", CancelDone: "U anulua. Gjithçka funksionon deri më {0}.",
            ResumeText: "Plani juaj mbaron më {0}.", ResumeButton: "Mbaj planin", Resumed: "Plani juaj rinovohet si zakonisht.",
            PdfInvoice: "Faturë", PdfFrom: "Nga", PdfTo: "Për", PdfDate: "Data", PdfDue: "Afati", PdfPeriod: "Periudha",
            PdfItem: "Artikulli", PdfQty: "Sasia", PdfUnit: "Çmimi për njësi", PdfAmount: "Vlera", PdfSubtotal: "Nëntotali", PdfVat: "TVSH {0}%",
            PdfNoVat: "Pa TVSH.", PdfTotal: "Totali", PdfPayTo: "Paguani me transfertë bankare te", PdfPaid: "Paguar më {0}", PdfPerBranch: "për pikë",
            EmailButton: "Hap faturimin",
            InvoiceSubject: "Fatura {0} nga My Quick Menu",
            InvoiceText: "Fatura juaj {0} është bashkëngjitur: {1}, me afat deri më {2}. Paguani me transfertë bankare në IBAN {3} me referencën {0}.",
            DueSoonSubject: "Fatura {0} skadon më {1}",
            DueSoonText: "Kujtesë: fatura {0} prej {1} skadon më {2}. Nëse e keni paguar tashmë, faleminderit, dhe injorojeni këtë email.",
            PaidSubject: "Pagesa u mor, faleminderit",
            PaidText: "Morëm pagesën për faturën {0} ({1}). Plani juaj është aktiv deri më {2}.",
            TrialSubject: "Prova juaj falas mbaron pas {0} ditësh", TrialSubjectOne: "Prova juaj falas mbaron nesër",
            TrialText: "Prova juaj mbaron më {0}. Zgjidhni si të paguani që gjithçka të vazhdojë; menutë mbeten online në çdo rast.",
            TrialEndedSubject: "Prova juaj falas mbaroi",
            TrialEndedText: "Menutë tuaja janë ende online, por paneli tani është vetëm për lexim dhe veçoritë me pagesë janë ndalur. Zgjidhni si të paguani që t'i rikthehen.",
            PastDueSubject: "Pagesa juaj është vonuar",
            PastDueText: "Nuk e kemi marrë pagesën për faturën {0}. Gjithçka funksionon deri më {1}; pas kësaj paneli kalon vetëm për lexim. Menutë mbeten online.",
            GraceSubject: "Llogaria juaj kalon vetëm për lexim më {0}",
            GraceText: "Fatura {0} është ende e papaguar. Më {1} paneli kalon vetëm për lexim dhe veçoritë me pagesë ndalen. Menutë mbeten online.",
            ReadOnlySubject: "Llogaria juaj tani është vetëm për lexim",
            ReadOnlyText: "Paneli është vetëm për lexim dhe veçoritë me pagesë janë ndalur. Paguani faturën e hapur që gjithçka të rikthehet. Menutë janë ende online.",
            CancelledSubject: "Plani juaj mbaroi",
            CancelledText: "Plani juaj mbaroi siç kërkuat. Menutë janë ende online; paneli është vetëm për lexim. Merrni një faturë të re kur të doni të vazhdoni.",
            PayCardTitle: "Paguani me kartë", PayCardIntro: "Paguhet sot dhe më pas automatikisht çdo periudhë. Periudha e paguar fillon sot.",
            PayCardButton: "Paguaj me kartë",
            CardNote: "Pagesat me kartë i përpunon Paddle, rishitësi ynë: Paddle tërheq pagesën nga karta, shton TVSH-në kur aplikohet dhe dërgon faturën.",
            ManageCard: "Ndrysho kartën ose shkarko faturat",
            ChangeHintCard: "Ndryshimet vlejnë menjëherë; diferenca për pjesën e mbetur të periudhës tërhiqet ose kthehet në kartë.",
            ChangeSavedCard: "U ruajt. Plani juaj u përditësua; diferenca për këtë periudhë tërhiqet ose kthehet.",
            ErrCard: "Pagesa me kartë nuk mund të fillonte tani. Provoni sërish pas një minute, ose paguani me transfertë bankare.",
            CardThanks: "Faleminderit! Pagesa juaj po konfirmohet; kjo faqe përditësohet brenda një minute.",
            PayPageTitle: "Paguani me kartë", PayPageText: "Formulari i sigurt i pagesës hapet këtu. Nëse nuk hapet, lejoni dritaret që hapen vetë dhe ringarkoni faqen.",
            PayPageBack: "Kthehu te faturimi",
            PastDueCardText: "Pagesa me kartë nuk kaloi. Do të provojmë sërish ditët në vijim; mund edhe ta ndryshoni kartën. Gjithçka funksionon deri më {0}; pas kësaj paneli kalon vetëm për lexim. Menutë mbeten online.",
            PaidByCard: "Paguar me kartë")
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];

    /// <summary>A date the same way on every server: "19 Oct 2026" in English, "19.10.2026" in Albanian.</summary>
    public static string Date(DateTime utc, string? language) =>
        language == "sq" ? utc.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The last day of a period that ends at midnight, for "paid until 18 Nov".</summary>
    public static string LastDay(DateTime endUtc, string? language) => Date(endUtc.AddSeconds(-1), language);

    public static string Status(Words w, InvoiceStatus status, DateTime due, DateTime utcNow) => status switch
    {
        InvoiceStatus.Paid => w.InvPaid,
        InvoiceStatus.Void => w.InvVoid,
        _ => due < utcNow ? w.InvOverdue : w.InvOpen
    };

    /// <summary>A module's name for invoices and the billing page (the signup funnel's words).</summary>
    public static string Module(string? language, BillingModule m) =>
        m == BillingModule.Menu ? SignupText.For(language).PresetMenu : SignupText.ModuleName(SignupText.For(language), m);
}
