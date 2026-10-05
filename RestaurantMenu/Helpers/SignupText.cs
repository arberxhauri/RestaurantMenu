using RestaurantMenu.Models;

namespace RestaurantMenu.Helpers;

/// <summary>
/// The signup funnel's words (pricing, signup, email confirmation, approval, onboarding, the trial
/// banner, and their emails) in English and Albanian. The rest of the back office is English.
/// Checked by TranslationCoverageTests: both languages, no blanks, the same {0} placeholders.
/// </summary>
public static class SignupText
{
    /// <summary>The funnel's languages (the guest pages have seven; these pages have two).</summary>
    public static readonly string[] Languages = { "en", "sq" };

    public record Words(
        string SwitchLanguage, string SwitchLanguageCode,
        // Pricing
        string PricingTitle, string PricingIntro, string PresetsTitle,
        string PresetMenu, string PresetMenuText, string PresetGuests, string PresetGuestsText, string PresetEverything, string PresetEverythingText,
        string BuildTitle, string MenuIncluded, string MenuIncludedText,
        string Ordering, string OrderingText, string Bookings, string BookingsText, string Website, string WebsiteText,
        string Management, string ManagementText, string OwnDomain, string OwnDomainText, string Needs,
        string Branches, string BranchesHint, string BranchOne, string BranchMany, string Monthly, string Yearly, string YearlySaving,
        string PerMonth, string PerYear, string PerBranch, string Each, string Total, string PriceOnRequest, string VatNote,
        string UpdatePrice, string StartTrial, string TrialNote, string FromPerMonth,
        // Signup
        string SignupTitle, string SignupIntro, string YourPlan, string ChangePlan,
        string FullName, string Email, string EmailHint, string Password, string PasswordHint,
        string Restaurant, string RestaurantHint, string LinkTaken, string Country,
        string TermsLabel, string TermsLink, string PrivacyLink, string CreateAccount, string HaveAccount, string SignIn,
        string ErrName, string ErrEmail, string ErrPassword, string ErrRestaurant, string ErrCountry, string ErrTerms,
        string ErrRemoved, string ErrExists, string ErrBusy, string ErrExpired, string Closed, string ContactUs,
        // Confirm email
        string CheckTitle, string CheckText, string CheckSpam, string Resend, string ResendDone, string EmailFailed,
        string VerifyTitle, string VerifyText, string VerifyButton, string VerifyInvalid, string VerifyAlready,
        string PendingTitle, string PendingText,
        string LoginUnconfirmed, string LoginPending,
        // Emails
        string VerifySubject, string VerifyPreheader, string VerifyParagraph, string VerifyEmailButton, string VerifyFooter,
        string ApprovedSubject, string ApprovedText, string ApprovedButton,
        // Onboarding and trial
        string OnbTitle, string OnbIntro, string OnbBranch, string OnbBranchText, string OnbMenu, string OnbMenuText,
        string OnbQr, string OnbQrText, string OnbDone, string OnbAllDone, string OnbHide, string OnbStart,
        string TrialDays, string TrialOneDay, string TrialToday, string TrialAfter, string TrialContact,
        string Photo);

    private static readonly Dictionary<string, Words> All = new()
    {
        ["en"] = new Words(
            SwitchLanguage: "Shqip", SwitchLanguageCode: "sq",
            PricingTitle: "Pricing",
            PricingIntro: "Start with the menu and add the rest when you need it. Every plan starts with a {0}-day free trial, no card needed.",
            PresetsTitle: "Popular starting points",
            PresetMenu: "Menu", PresetMenuText: "QR menus in 7 languages, allergens, photos, specials and guest feedback.",
            PresetGuests: "Menu, website and bookings", PresetGuestsText: "Your own website and online bookings, on top of the menu.",
            PresetEverything: "Everything", PresetEverythingText: "Table ordering, the kitchen display, stock, shifts and sales too.",
            BuildTitle: "Build your plan",
            MenuIncluded: "Menu (always included)", MenuIncludedText: "QR menus, translations, allergens, offline menu, analytics and feedback.",
            Ordering: "Table ordering", OrderingText: "Guests send their order from the table to the kitchen display.",
            Bookings: "Bookings", BookingsText: "A booking page and a day view for your team.",
            Website: "Website", WebsiteText: "A one-page website with your menu, hours and map, on a free address.",
            Management: "Management", ManagementText: "Stock and recipes, staff shifts and end-of-day sales.",
            OwnDomain: "Own domain", OwnDomainText: "Your website on your own address, e.g. www.yourrestaurant.al.",
            Needs: "needs {0}",
            Branches: "Branches", BranchesHint: "Each location has its own menu, link and QR code.",
            BranchOne: "1 branch", BranchMany: "{0} branches",
            Monthly: "Monthly", Yearly: "Yearly", YearlySaving: "You save {0} a year.",
            PerMonth: "/ month", PerYear: "/ year", PerBranch: "per branch", Each: "each",
            Total: "Total", PriceOnRequest: "Price on request",
            VatNote: "Prices without VAT. VAT is added on the invoice where it applies.",
            UpdatePrice: "Update price", StartTrial: "Start free trial",
            TrialNote: "{0} days free, no card needed. Your menus stay online after the trial.",
            FromPerMonth: "from {0} a month",
            SignupTitle: "Create your account",
            SignupIntro: "{0} days free, no card needed. You'll confirm your email next.",
            YourPlan: "Your plan", ChangePlan: "Change",
            FullName: "Your name", Email: "Email", EmailHint: "You'll sign in with it, and we'll send the confirmation link here.",
            Password: "Password", PasswordHint: "At least 8 characters, with a capital letter, a small letter and a number.",
            Restaurant: "Restaurant name", RestaurantHint: "Your menu link: {0}",
            LinkTaken: "That link is taken, so yours will be {0}. You can change the name later.",
            Country: "Country",
            TermsLabel: "I accept the {0} and have read the {1}.", TermsLink: "terms of use", PrivacyLink: "privacy policy",
            CreateAccount: "Create account", HaveAccount: "Already have an account?", SignIn: "Sign in",
            ErrName: "Enter your name.", ErrEmail: "Enter a valid email address.",
            ErrPassword: "Choose a password of at least 8 characters, with a capital letter, a small letter and a number.",
            ErrRestaurant: "Enter your restaurant's name (2 to 80 characters).", ErrCountry: "Choose your country.",
            ErrTerms: "Accept the terms of use to continue.",
            ErrRemoved: "This email was used for an account before. Contact us and we'll reopen it.",
            ErrExists: "There's already an account with this email. Sign in, or reset your password if you forgot it.",
            ErrBusy: "Too many signups from this connection. Try again in an hour.",
            ErrExpired: "This form was open for a long time. Check your details and send it again.",
            Closed: "New accounts can't be created online right now. Contact us and we'll set one up for you.",
            ContactUs: "Contact us",
            CheckTitle: "Check your email",
            CheckText: "We sent a link to {0}. Open it to confirm your email and start your free trial. It works for 3 days.",
            CheckSpam: "Nothing after a few minutes? Check your spam folder, or send it again.",
            Resend: "Send the link again", ResendDone: "If that address is waiting to be confirmed, a new link is on its way.",
            EmailFailed: "We couldn't send the email just now. Try sending it again in a minute.",
            VerifyTitle: "Confirm your email", VerifyText: "Confirm {0} to open your account.",
            VerifyButton: "Confirm and continue",
            VerifyInvalid: "This link doesn't work any more. Links work for 3 days: sign in to get a new one, or sign up again.",
            VerifyAlready: "Your email is already confirmed. Sign in to continue.",
            PendingTitle: "Thanks, we're on it",
            PendingText: "Your email is confirmed. We look at every new account before it opens. You'll get an email as soon as yours is ready, usually within a day.",
            LoginUnconfirmed: "Confirm your email first: we sent a link to {0}.",
            LoginPending: "We're still looking at your account. You'll get an email as soon as it's ready.",
            VerifySubject: "Confirm your email for My Quick Menu",
            VerifyPreheader: "One click and your free trial starts.",
            VerifyParagraph: "Thanks for signing up {0}. Confirm this is your email address and you can set up your menu straight away.",
            VerifyEmailButton: "Confirm my email",
            VerifyFooter: "The link works for 3 days. If you didn't sign up, ignore this email and nothing happens.",
            ApprovedSubject: "Your My Quick Menu account is ready",
            ApprovedText: "Your account is open and your {0}-day free trial has started. Sign in to create your first menu.",
            ApprovedButton: "Sign in",
            OnbTitle: "Get your menu live", OnbIntro: "Three steps, about ten minutes.",
            OnbBranch: "Create your branch", OnbBranchText: "Name, address and logo. Your menu link is ready straight away.",
            OnbMenu: "Add a category and a few dishes", OnbMenuText: "Prices, photos and allergens. Translations can come later.",
            OnbQr: "Open your menu and print the QR code", OnbQrText: "Put it on the tables and guests can scan it.",
            OnbDone: "Done", OnbAllDone: "Your menu is live. Well done!", OnbHide: "Hide this list", OnbStart: "Start",
            TrialDays: "{0} days left in your free trial.", TrialOneDay: "1 day left in your free trial.", TrialToday: "Your free trial ends today.",
            TrialAfter: "After it ends your menus stay online and the back office becomes read-only.",
            TrialContact: "Contact us to keep everything running",
            Photo: "Plates of food shared across a dark wooden table"),

        ["sq"] = new Words(
            SwitchLanguage: "English", SwitchLanguageCode: "en",
            PricingTitle: "Çmimet",
            PricingIntro: "Filloni me menunë dhe shtoni pjesën tjetër kur t'ju duhet. Çdo plan fillon me {0} ditë provë falas, pa kartë.",
            PresetsTitle: "Zgjedhjet më të shpeshta",
            PresetMenu: "Menuja", PresetMenuText: "Menu me QR në 7 gjuhë, alergjenë, foto, oferta dhe vlerësime nga klientët.",
            PresetGuests: "Menu, faqe interneti dhe rezervime", PresetGuestsText: "Faqja juaj e internetit dhe rezervimet online, përveç menusë.",
            PresetEverything: "Të gjitha", PresetEverythingText: "Edhe porositë nga tavolina, ekrani i kuzhinës, magazina, turnet dhe shitjet.",
            BuildTitle: "Ndërtoni planin tuaj",
            MenuIncluded: "Menuja (gjithmonë e përfshirë)", MenuIncludedText: "Menu me QR, përkthime, alergjenë, menu pa internet, statistika dhe vlerësime.",
            Ordering: "Porositë nga tavolina", OrderingText: "Klientët e dërgojnë porosinë nga tavolina te ekrani i kuzhinës.",
            Bookings: "Rezervimet", BookingsText: "Një faqe rezervimesh dhe pamja e ditës për stafin.",
            Website: "Faqja e internetit", WebsiteText: "Një faqe me menunë, orarin dhe hartën, në një adresë falas.",
            Management: "Menaxhimi", ManagementText: "Magazina dhe recetat, turnet e stafit dhe shitjet e ditës.",
            OwnDomain: "Domeni juaj", OwnDomainText: "Faqja juaj në adresën tuaj, p.sh. www.restoranti-juaj.al.",
            Needs: "kërkon {0}",
            Branches: "Pikat", BranchesHint: "Çdo pikë ka menunë, lidhjen dhe kodin e vet QR.",
            BranchOne: "1 pikë", BranchMany: "{0} pika",
            Monthly: "Mujore", Yearly: "Vjetore", YearlySaving: "Kurseni {0} në vit.",
            PerMonth: "/ muaj", PerYear: "/ vit", PerBranch: "për pikë", Each: "secili",
            Total: "Gjithsej", PriceOnRequest: "Çmimi me kërkesë",
            VatNote: "Çmimet pa TVSH. TVSH-ja shtohet në faturë kur aplikohet.",
            UpdatePrice: "Përditëso çmimin", StartTrial: "Filloni provën falas",
            TrialNote: "{0} ditë falas, pa kartë. Menutë tuaja mbeten online edhe pas provës.",
            FromPerMonth: "nga {0} në muaj",
            SignupTitle: "Krijoni llogarinë",
            SignupIntro: "{0} ditë falas, pa kartë. Më pas do të konfirmoni email-in.",
            YourPlan: "Plani juaj", ChangePlan: "Ndrysho",
            FullName: "Emri juaj", Email: "Email", EmailHint: "Me të do të hyni në llogari, dhe këtu do t'ju dërgojmë lidhjen e konfirmimit.",
            Password: "Fjalëkalimi", PasswordHint: "Të paktën 8 karaktere, me një shkronjë të madhe, një të vogël dhe një numër.",
            Restaurant: "Emri i restorantit", RestaurantHint: "Lidhja e menusë suaj: {0}",
            LinkTaken: "Kjo lidhje është e zënë, ndaj e juaja do të jetë {0}. Emrin mund ta ndryshoni më vonë.",
            Country: "Shteti",
            TermsLabel: "Pranoj {0} dhe kam lexuar {1}.", TermsLink: "kushtet e përdorimit", PrivacyLink: "politikën e privatësisë",
            CreateAccount: "Krijo llogarinë", HaveAccount: "Keni tashmë një llogari?", SignIn: "Hyni",
            ErrName: "Shkruani emrin tuaj.", ErrEmail: "Shkruani një adresë email të vlefshme.",
            ErrPassword: "Zgjidhni një fjalëkalim me të paktën 8 karaktere, me një shkronjë të madhe, një të vogël dhe një numër.",
            ErrRestaurant: "Shkruani emrin e restorantit (2 deri në 80 karaktere).", ErrCountry: "Zgjidhni shtetin.",
            ErrTerms: "Pranoni kushtet e përdorimit për të vazhduar.",
            ErrRemoved: "Ky email është përdorur më parë për një llogari. Na kontaktoni dhe do ta rihapim.",
            ErrExists: "Ekziston tashmë një llogari me këtë email. Hyni, ose rivendosni fjalëkalimin nëse e keni harruar.",
            ErrBusy: "Shumë regjistrime nga kjo lidhje. Provoni sërish pas një ore.",
            ErrExpired: "Ky formular qëndroi i hapur për shumë kohë. Kontrolloni të dhënat dhe dërgojeni sërish.",
            Closed: "Për momentin llogaritë e reja nuk mund të krijohen online. Na kontaktoni dhe do t'jua krijojmë ne.",
            ContactUs: "Na kontaktoni",
            CheckTitle: "Kontrolloni email-in",
            CheckText: "Dërguam një lidhje te {0}. Hapeni për të konfirmuar email-in dhe për të filluar provën falas. Vlen për 3 ditë.",
            CheckSpam: "Nuk ka ardhur asgjë pas disa minutash? Shikoni te spam-i, ose dërgojeni sërish.",
            Resend: "Dërgoje sërish lidhjen", ResendDone: "Nëse kjo adresë pret konfirmimin, një lidhje e re po vjen.",
            EmailFailed: "Nuk arritëm ta dërgojmë email-in tani. Provoni ta dërgoni sërish pas një minute.",
            VerifyTitle: "Konfirmoni email-in", VerifyText: "Konfirmoni {0} për të hapur llogarinë.",
            VerifyButton: "Konfirmo dhe vazhdo",
            VerifyInvalid: "Kjo lidhje nuk funksionon më. Lidhjet vlejnë 3 ditë: hyni për të marrë një të re, ose regjistrohuni sërish.",
            VerifyAlready: "Email-i juaj është konfirmuar tashmë. Hyni për të vazhduar.",
            PendingTitle: "Faleminderit, po merremi me të",
            PendingText: "Email-i juaj u konfirmua. Ne e shikojmë çdo llogari të re para se të hapet. Do të merrni një email sapo e juaja të jetë gati, zakonisht brenda një dite.",
            LoginUnconfirmed: "Konfirmoni fillimisht email-in: dërguam një lidhje te {0}.",
            LoginPending: "Ende po e shikojmë llogarinë tuaj. Do të merrni një email sapo të jetë gati.",
            VerifySubject: "Konfirmoni email-in për My Quick Menu",
            VerifyPreheader: "Një klikim dhe prova juaj falas fillon.",
            VerifyParagraph: "Faleminderit që u regjistruat me {0}. Konfirmoni që ky është email-i juaj dhe mund ta përgatitni menunë menjëherë.",
            VerifyEmailButton: "Konfirmo email-in",
            VerifyFooter: "Lidhja vlen për 3 ditë. Nëse nuk jeni regjistruar ju, injorojeni këtë email dhe nuk ndodh asgjë.",
            ApprovedSubject: "Llogaria juaj në My Quick Menu është gati",
            ApprovedText: "Llogaria juaj është hapur dhe prova falas prej {0} ditësh ka filluar. Hyni për të krijuar menunë e parë.",
            ApprovedButton: "Hyni",
            OnbTitle: "Vendosni menunë online", OnbIntro: "Tre hapa, rreth dhjetë minuta.",
            OnbBranch: "Krijoni pikën tuaj", OnbBranchText: "Emri, adresa dhe logoja. Lidhja e menusë është gati menjëherë.",
            OnbMenu: "Shtoni një kategori dhe disa pjata", OnbMenuText: "Çmimet, fotot dhe alergjenët. Përkthimet mund të vijnë më vonë.",
            OnbQr: "Hapni menunë dhe printoni kodin QR", OnbQrText: "Vendoseni në tavolina dhe klientët mund ta skanojnë.",
            OnbDone: "Gati", OnbAllDone: "Menuja juaj është online. Të lumtë!", OnbHide: "Fshihe këtë listë", OnbStart: "Fillo",
            TrialDays: "Ju kanë mbetur {0} ditë nga prova falas.", TrialOneDay: "Ju ka mbetur 1 ditë nga prova falas.", TrialToday: "Prova juaj falas mbaron sot.",
            TrialAfter: "Pasi të mbarojë, menutë mbeten online dhe paneli kalon vetëm për lexim.",
            TrialContact: "Na kontaktoni që gjithçka të vazhdojë",
            Photo: "Pjata me ushqim të ndara mbi një tavolinë druri të errët")
    };

    /// <summary>Country choices at signup (ISO code → name), in the order shown.</summary>
    private static readonly Dictionary<string, (string Code, string Name)[]> Countries = new()
    {
        ["en"] = new[] { ("AL", "Albania"), ("XK", "Kosovo"), ("MK", "North Macedonia"), ("ME", "Montenegro"), ("GR", "Greece"), ("IT", "Italy"), ("ZZ", "Another country") },
        ["sq"] = new[] { ("AL", "Shqipëri"), ("XK", "Kosovë"), ("MK", "Maqedoni e Veriut"), ("ME", "Mal i Zi"), ("GR", "Greqi"), ("IT", "Itali"), ("ZZ", "Një shtet tjetër") }
    };

    public static Words For(string? language) => All.TryGetValue(language ?? "en", out var w) ? w : All["en"];

    public static IReadOnlyList<(string Code, string Name)> CountriesFor(string? language) =>
        Countries.TryGetValue(language ?? "en", out var c) ? c : Countries["en"];

    public static bool IsCountry(string? code) => code != null && Countries["en"].Any(c => c.Code == code);

    /// <summary>en or sq: what was asked for, else the browser's preference (Albanian browsers get Albanian), else English.</summary>
    public static string Pick(string? requested, string? acceptLanguage)
    {
        if (requested is "en" or "sq") return requested;
        return (acceptLanguage ?? "").TrimStart().StartsWith("sq", StringComparison.OrdinalIgnoreCase) ? "sq" : "en";
    }

    public static string ModuleName(Words w, BillingModule m) => m switch
    {
        BillingModule.Menu => w.MenuIncluded,
        BillingModule.Ordering => w.Ordering,
        BillingModule.Bookings => w.Bookings,
        BillingModule.Website => w.Website,
        BillingModule.Management => w.Management,
        BillingModule.OwnDomain => w.OwnDomain,
        _ => m.ToString()
    };

    public static string ModuleText(Words w, BillingModule m) => m switch
    {
        BillingModule.Menu => w.MenuIncludedText,
        BillingModule.Ordering => w.OrderingText,
        BillingModule.Bookings => w.BookingsText,
        BillingModule.Website => w.WebsiteText,
        BillingModule.Management => w.ManagementText,
        BillingModule.OwnDomain => w.OwnDomainText,
        _ => ""
    };

    public static (string Title, string Text) Preset(Words w, string key) => key switch
    {
        "guests" => (w.PresetGuests, w.PresetGuestsText),
        "everything" => (w.PresetEverything, w.PresetEverythingText),
        _ => (w.PresetMenu, w.PresetMenuText)
    };

    /// <summary>The trial line for the banner: "3 days left…", "1 day left…", "ends today".</summary>
    public static string TrialLeft(Words w, int days) => days switch
    {
        <= 0 => w.TrialToday,
        1 => w.TrialOneDay,
        _ => string.Format(w.TrialDays, days)
    };
}
