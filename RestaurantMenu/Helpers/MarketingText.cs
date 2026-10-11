namespace RestaurantMenu.Helpers;

/// <summary>
/// The marketing site's words in English and Albanian: the header and footer every marketing
/// page shares, and the home page. Albanian is the main market, so the Albanian is written for
/// Albanian restaurant owners, not translated word for word. Landing pages keep their own copy
/// in Helpers/Landing (LandingContent).
/// </summary>
public sealed class MarketingText
{
    public required string Lang { get; init; }

    // ---------------------------------------------------------------- brand line (GEO)
    /// <summary>
    /// The one-sentence definition, word for word the same on every page, in llms.txt and in the
    /// Organization schema, so assistants can quote it. {0} = the lowest monthly price ("€5").
    /// </summary>
    public required string Definition { get; init; }
    /// <summary>The same without a price, for when no prices are set.</summary>
    public required string DefinitionNoPrice { get; init; }

    // ---------------------------------------------------------------- chrome
    public required string SkipToContent { get; init; }
    public required string HomeLabel { get; init; }
    public required string NavPrimary { get; init; }
    public required string NavProducts { get; init; }
    public required string NavFeatures { get; init; }
    public required string NavHow { get; init; }
    public required string NavPricing { get; init; }
    public required string NavPartners { get; init; }
    public required string Dashboard { get; init; }
    public required string SignIn { get; init; }
    public required string StartTrial { get; init; }
    public required string GetInTouch { get; init; }
    public required string OpenNav { get; init; }
    public required string CloseNav { get; init; }
    /// <summary>The other language's name, in that language.</summary>
    public required string OtherLanguageName { get; init; }
    public required string OtherLanguageCode { get; init; }
    public required string FooterTag { get; init; }
    public required string FooterNav { get; init; }
    public required string Privacy { get; init; }
    public required string Terms { get; init; }
    public required string NavDigitalMenu { get; init; }
    public required string NavWebsite { get; init; }
    public required string NavBookings { get; init; }
    public required string NavManagement { get; init; }
    public required string NavRestaurants { get; init; }
    public required string NavFaq { get; init; }
    public required string NavAbout { get; init; }
    public required string NavContact { get; init; }
    public required string FooterProducts { get; init; }
    public required string FooterCompany { get; init; }
    public required string FooterLegal { get; init; }
    public required string Home { get; init; }
    public required string Breadcrumbs { get; init; }
    public required string LastUpdated { get; init; }
    public required string HowItWorks { get; init; }

    // ---------------------------------------------------------------- restaurant directory
    public required string DirectoryTitle { get; init; }
    public required string DirectoryMetaTitle { get; init; }
    /// <summary>{0} = number of restaurants.</summary>
    public required string DirectoryDescription { get; init; }
    public required string DirectoryIntro { get; init; }
    public required string DirectoryOtherCity { get; init; }
    public required string DirectoryEmpty { get; init; }
    public required string DirectoryMenu { get; init; }
    public required string DirectoryWebsite { get; init; }
    public required string DirectoryBook { get; init; }
    public required string DirectoryJoinTitle { get; init; }
    public required string DirectoryJoinText { get; init; }
    public required string PagePrevious { get; init; }
    public required string PageNext { get; init; }
    /// <summary>{0} = page, {1} = pages.</summary>
    public required string PageOf { get; init; }

    // ---------------------------------------------------------------- home: meta
    public required string HomeTitle { get; init; }
    /// <summary>{0} = trial days.</summary>
    public required string HomeDescription { get; init; }

    // ---------------------------------------------------------------- home: hero and story
    public required string HeroTitle { get; init; }
    public required string HeroLede { get; init; }
    public required string SeeHow { get; init; }
    public required string HeroImageAlt { get; init; }
    public required string StoryTitle { get; init; }
    public required string StoryText { get; init; }
    public required string DeviceLabel { get; init; }
    public required string Callout1Title { get; init; }
    public required string Callout1Text { get; init; }
    public required string Callout2Title { get; init; }
    public required string Callout2Text { get; init; }
    public required string Callout3Title { get; init; }
    public required string Callout3Text { get; init; }

    // ---------------------------------------------------------------- home: partners
    public required string PartnersTitle { get; init; }
    /// <summary>{0} = restaurant name.</summary>
    public required string PartnerMenu { get; init; }

    // ---------------------------------------------------------------- home: products
    public required string ProductsTitle { get; init; }
    public required string ProductsText { get; init; }
    public required string MenusTitle { get; init; }
    public required string MenusText { get; init; }
    public required string[] MenusTags { get; init; }
    public required string MenusImageAlt { get; init; }
    public required string OpsTitle { get; init; }
    public required string OpsText { get; init; }
    public required string[] OpsTags { get; init; }
    public required string OpsImageAlt { get; init; }
    public required string WebTitle { get; init; }
    public required string WebText { get; init; }
    public required string[] WebTags { get; init; }
    public required string WebImageAlt { get; init; }
    public required string BookTitle { get; init; }
    public required string BookText { get; init; }
    public required string SlotsLabel { get; init; }
    /// <summary>{0} = the time picked.</summary>
    public required string SlotNote { get; init; }
    public required string BookImageAlt { get; init; }
    public required string LearnMore { get; init; }

    // ---------------------------------------------------------------- home: features
    public required string FeaturesTitle { get; init; }
    public required string FeaturesText { get; init; }
    public required (string Title, string Text)[] Features { get; init; }

    // ---------------------------------------------------------------- home: steps
    public required string StepsTitle { get; init; }
    public required (string Title, string Text, string ImageAlt)[] Steps { get; init; }

    // ---------------------------------------------------------------- home: pricing and CTA
    public required string PricingTitle { get; init; }
    /// <summary>{0} = trial days.</summary>
    public required string PricingText { get; init; }
    public required string SeePricing { get; init; }
    public required string BuildOwnPlan { get; init; }
    public required string CtaTitle { get; init; }
    public required string CtaText { get; init; }
    /// <summary>{0} = trial days.</summary>
    public required string CtaTrial { get; init; }
    public required string CtaContact { get; init; }

    public static MarketingText For(string? language) => language == "sq" ? Sq : En;

    public static readonly MarketingText En = new()
    {
        Lang = "en",
        Definition = "My Quick Menu is a restaurant platform from Tirana, Albania that gives restaurants a QR digital menu, a website and online table bookings from one dashboard, starting at {0} a month.",
        DefinitionNoPrice = "My Quick Menu is a restaurant platform from Tirana, Albania that gives restaurants a QR digital menu, a website and online table bookings from one dashboard.",

        SkipToContent = "Skip to content",
        HomeLabel = "My Quick Menu, home",
        NavPrimary = "Primary",
        NavProducts = "Products",
        NavFeatures = "Features",
        NavHow = "How it works",
        NavPricing = "Pricing",
        NavPartners = "Partners",
        Dashboard = "Dashboard",
        SignIn = "Sign in",
        StartTrial = "Start free trial",
        GetInTouch = "Get in touch",
        OpenNav = "Open menu",
        CloseNav = "Close menu",
        OtherLanguageName = "Shqip",
        OtherLanguageCode = "sq",
        FooterTag = "Menus, websites & bookings",
        FooterNav = "Footer",
        Privacy = "Privacy",
        Terms = "Terms",
        NavDigitalMenu = "Digital menu",
        NavWebsite = "Restaurant website",
        NavBookings = "Table bookings",
        NavManagement = "Management",
        NavRestaurants = "Restaurants",
        NavFaq = "FAQ",
        NavAbout = "About",
        NavContact = "Contact",
        FooterProducts = "Products",
        FooterCompany = "Company",
        FooterLegal = "Legal",
        Home = "Home",
        Breadcrumbs = "Breadcrumbs",
        LastUpdated = "Last updated",
        HowItWorks = "How it works",

        DirectoryTitle = "Restaurants with online menus",
        DirectoryMetaTitle = "Restaurants with Online Menus · My Quick Menu",
        DirectoryDescription = "Browse {0} restaurants, cafés and bars with an online menu on My Quick Menu, by city: current prices, photos, allergens and online booking.",
        DirectoryIntro = "Restaurants, cafés and bars that publish their menu with My Quick Menu, grouped by city. Every menu shows the restaurant's current prices, photos and allergens, kept up to date by the restaurant itself.",
        DirectoryOtherCity = "Other places",
        DirectoryEmpty = "No restaurants to show yet.",
        DirectoryMenu = "Menu",
        DirectoryWebsite = "Website",
        DirectoryBook = "Book",
        DirectoryJoinTitle = "Run a restaurant?",
        DirectoryJoinText = "Put your menu online and appear here too.",
        PagePrevious = "Previous",
        PageNext = "Next",
        PageOf = "Page {0} of {1}",

        HomeTitle = "My Quick Menu: QR menus, websites & bookings for restaurants",
        HomeDescription = "QR digital menus, a restaurant website and online table bookings from one dashboard. Made in Tirana for restaurants, cafés and bars. {0}-day free trial.",

        HeroTitle = "The whole restaurant, one platform",
        HeroLede = "Digital menus, your website, bookings and the back office on one set of data. Change a price once and it lands everywhere.",
        SeeHow = "See how it works",
        HeroImageAlt = "Friends sharing breakfast plates around a wooden table",
        StoryTitle = "Change it once. It's on every table.",
        StoryText = "One menu lives behind every QR code, so a price, a language or a sold-out dish updates wherever guests are looking.",
        DeviceLabel = "Example of a guest's menu",
        Callout1Title = "Price changed in the dashboard",
        Callout1Text = "The next guest to open the menu sees the new price.",
        Callout2Title = "Guest switched to Shqip",
        Callout2Text = "Seven languages, written once by you.",
        Callout3Title = "Marked as sold out",
        Callout3Text = "Guests can no longer order it from the table.",

        PartnersTitle = "Restaurants already serving with My Quick Menu",
        PartnerMenu = "{0} menu",

        ProductsTitle = "Four products, one back office",
        ProductsText = "Start with the menu and add the rest when you need it. Dishes, hours and branches are entered once and shared across everything.",
        MenusTitle = "Digital menus",
        MenusText = "QR menus guests scan at the table. Multilingual, photo-rich, allergen-aware and editable mid-service, with orders sent straight to the kitchen.",
        MenusTags = new[] { "QR & table tents", "Table ordering", "Allergens" },
        MenusImageAlt = "A guest enjoying dinner in a warmly lit restaurant",
        OpsTitle = "Management system",
        OpsText = "Kitchen display, stock, staff shifts and end-of-day sales in one dashboard. Multi-branch from day one, with every branch on one overview.",
        OpsTags = new[] { "Kitchen display", "Stock & recipes", "Shifts & sales" },
        OpsImageAlt = "A chef finishing plates under copper pass lights",
        WebTitle = "Restaurant websites",
        WebText = "A fast site on a free subdomain or your own domain. It pulls its dishes, photos and hours straight from your dashboard, so it's never out of date.",
        WebTags = new[] { "Free subdomain", "Own domain", "SEO ready" },
        WebImageAlt = "A bright café dining room with wooden tables",
        BookTitle = "Bookings",
        BookText = "Take reservations from the site, the menu QR or your Instagram link. Confirmations and reminders by email, no-shows tracked.",
        SlotsLabel = "Try it: pick a time for Friday",
        SlotNote = "Table for 4 held at {0}. The confirmation goes out by email.",
        BookImageAlt = "A seaside restaurant terrace set for dinner",
        LearnMore = "Learn more",

        FeaturesTitle = "Shared across every product",
        FeaturesText = "Built for the way restaurants actually work: mid-service edits, seasonal swaps, and guests who don't all speak your language.",
        Features = new[]
        {
            ("Multilingual support", "Serve international guests in their own language. They switch in one tap; you write it once."),
            ("Mobile optimised", "Menus built phone-first: legible in low light, thumb-reachable, and once opened they keep working when the Wi-Fi drops."),
            ("Instant updates", "Sold out at 19:40? One tap. The next guest to open the menu sees it, and table orders for it are refused."),
            ("Custom branding", "Your colours, your logo, your banner. The menu looks like your room, not like software."),
            ("Easy management", "Branches, categories and dishes in one dashboard. Drag to reorder, no training needed."),
            ("Secure & reliable", "Encrypted connections, role-based access, and deleted branches you can recover. Your menu is up when your doors are.")
        },

        StepsTitle = "Three steps to going live",
        Steps = new[]
        {
            ("Create your branch", "Name, address, logo and banner. Each location gets its own menu, its own colours and its own link.", "An empty dining room ready for service"),
            ("Add your menu", "Categories, dishes, prices, photos and allergens. Translate each dish once and every guest reads it in their language.", "A chef chopping herbs on a board"),
            ("Switch on the rest", "Print the QR, publish the website, open the booking calendar. Same data, three new front doors.", "A plated dish on a set table")
        },

        PricingTitle = "Pay for what you use",
        PricingText = "Every plan starts with the menu. Add the rest per branch, switch it off when you don't need it. {0} days free, no card needed.",
        SeePricing = "See pricing",
        BuildOwnPlan = "Build your own plan",
        CtaTitle = "Run the whole room from one place",
        CtaText = "Menus, website, bookings and back office. Start with one, add the rest whenever.",
        CtaTrial = "Try it free for {0} days, no card needed.",
        CtaContact = "Tell us about your restaurant and we'll set up your account."
    };

    public static readonly MarketingText Sq = new()
    {
        Lang = "sq",
        Definition = "My Quick Menu është një platformë për restorante nga Tirana, që u jep restoranteve menu dixhitale me QR, faqe interneti dhe rezervime online nga një panel i vetëm, duke filluar nga {0} në muaj.",
        DefinitionNoPrice = "My Quick Menu është një platformë për restorante nga Tirana, që u jep restoranteve menu dixhitale me QR, faqe interneti dhe rezervime online nga një panel i vetëm.",

        SkipToContent = "Kalo te përmbajtja",
        HomeLabel = "My Quick Menu, faqja kryesore",
        NavPrimary = "Navigimi kryesor",
        NavProducts = "Produktet",
        NavFeatures = "Veçoritë",
        NavHow = "Si funksionon",
        NavPricing = "Çmimet",
        NavPartners = "Restorantet",
        Dashboard = "Paneli",
        SignIn = "Hyr",
        StartTrial = "Provojeni falas",
        GetInTouch = "Na kontaktoni",
        OpenNav = "Hap navigimin",
        CloseNav = "Mbyll navigimin",
        OtherLanguageName = "English",
        OtherLanguageCode = "en",
        FooterTag = "Menu, faqe interneti dhe rezervime",
        FooterNav = "Lidhje në fund të faqes",
        Privacy = "Privatësia",
        Terms = "Kushtet",
        NavDigitalMenu = "Menu dixhitale",
        NavWebsite = "Faqe interneti",
        NavBookings = "Rezervime",
        NavManagement = "Menaxhimi",
        NavRestaurants = "Restorantet",
        NavFaq = "Pyetje",
        NavAbout = "Rreth nesh",
        NavContact = "Kontakt",
        FooterProducts = "Produktet",
        FooterCompany = "Kompania",
        FooterLegal = "Ligjore",
        Home = "Kryefaqja",
        Breadcrumbs = "Navigimi",
        LastUpdated = "Përditësuar më",
        HowItWorks = "Si funksionon",

        DirectoryTitle = "Restorante me menu online",
        DirectoryMetaTitle = "Restorante me menu online · My Quick Menu",
        DirectoryDescription = "Shfletoni {0} restorante, kafene dhe bare me menu online në My Quick Menu, sipas qytetit: çmime të sakta, foto, alergjenë dhe rezervim online.",
        DirectoryIntro = "Restorante, kafene dhe bare që e publikojnë menunë me My Quick Menu, të renditura sipas qytetit. Çdo menu tregon çmimet, fotot dhe alergjenët e sakta, të mbajtura të përditësuara nga vetë restoranti.",
        DirectoryOtherCity = "Vende të tjera",
        DirectoryEmpty = "Ende nuk ka restorante për të shfaqur.",
        DirectoryMenu = "Menuja",
        DirectoryWebsite = "Faqja",
        DirectoryBook = "Rezervo",
        DirectoryJoinTitle = "Keni restorant?",
        DirectoryJoinText = "Vendoseni menunë online dhe shfaquni edhe ju këtu.",
        PagePrevious = "E mëparshmja",
        PageNext = "Tjetra",
        PageOf = "Faqja {0} nga {1}",

        HomeTitle = "My Quick Menu: menu dixhitale me QR për restorante",
        HomeDescription = "Menu dixhitale me QR, faqe interneti dhe rezervime online për restorantin tuaj, nga një panel i vetëm. Bërë në Tiranë. Provë falas për {0} ditë.",

        HeroTitle = "I gjithë restoranti, në një platformë",
        HeroLede = "Menuja dixhitale, faqja e internetit, rezervimet dhe menaxhimi, të gjitha mbi të njëjtat të dhëna. Ndryshoni një çmim një herë dhe ai përditësohet kudo.",
        SeeHow = "Shikoni si funksionon",
        HeroImageAlt = "Miq që ndajnë pjatat e mëngjesit rreth një tavoline druri",
        StoryTitle = "E ndryshoni një herë. Shfaqet në çdo tavolinë.",
        StoryText = "Pas çdo kodi QR është e njëjta menu, ndaj një çmim, një gjuhë apo një pjatë që ka mbaruar përditësohet kudo ku po shikojnë klientët.",
        DeviceLabel = "Shembull i menusë që sheh klienti",
        Callout1Title = "Çmimi u ndryshua në panel",
        Callout1Text = "Klienti tjetër që hap menunë sheh çmimin e ri.",
        Callout2Title = "Klienti kaloi në shqip",
        Callout2Text = "Shtatë gjuhë, që i shkruani vetëm një herë.",
        Callout3Title = "U shënua si e mbaruar",
        Callout3Text = "Klientët nuk mund ta porosisin më nga tavolina.",

        PartnersTitle = "Restorante që punojnë tashmë me My Quick Menu",
        PartnerMenu = "Menuja e {0}",

        ProductsTitle = "Katër produkte, një panel i vetëm",
        ProductsText = "Filloni me menunë dhe shtoni pjesën tjetër kur t'ju duhet. Pjatat, orari dhe degët shkruhen një herë dhe përdoren kudo.",
        MenusTitle = "Menu dixhitale",
        MenusText = "Menu me QR që klientët i skanojnë në tavolinë. Në shumë gjuhë, me foto, me alergjenë dhe të ndryshueshme gjatë shërbimit, me porositë që shkojnë drejt në kuzhinë.",
        MenusTags = new[] { "QR dhe mbajtëse tavoline", "Porosi nga tavolina", "Alergjenët" },
        MenusImageAlt = "Një klient që shijon darkën në një restorant me dritë të ngrohtë",
        OpsTitle = "Sistemi i menaxhimit",
        OpsText = "Ekrani i kuzhinës, magazina, turnet e stafit dhe shitjet ditore në një panel. Me shumë degë që në ditën e parë, të gjitha në një pamje të vetme.",
        OpsTags = new[] { "Ekrani i kuzhinës", "Magazina dhe recetat", "Turnet dhe shitjet" },
        OpsImageAlt = "Një kuzhinier që përfundon pjatat nën dritat e kuzhinës",
        WebTitle = "Faqe interneti për restorante",
        WebText = "Një faqe e shpejtë në një nëndomain falas ose në domainin tuaj. Pjatat, fotot dhe orari merren drejt nga paneli, ndaj nuk del kurrë e vjetruar.",
        WebTags = new[] { "Nëndomain falas", "Domaini juaj", "Gati për Google" },
        WebImageAlt = "Një kafene e ndriçuar me tavolina druri",
        BookTitle = "Rezervimet",
        BookText = "Merrni rezervime nga faqja, nga QR-ja e menusë ose nga linku në Instagram. Konfirmime dhe kujtesa me email, dhe evidencë për klientët që nuk vijnë.",
        SlotsLabel = "Provojeni: zgjidhni një orë për të premten",
        SlotNote = "Tavolina për 4 u mbajt për në {0}. Konfirmimi dërgohet me email.",
        BookImageAlt = "Një tarracë restoranti buzë detit, gati për darkë",
        LearnMore = "Mësoni më shumë",

        FeaturesTitle = "Të përbashkëta për çdo produkt",
        FeaturesText = "Ndërtuar për mënyrën si punojnë vërtet restorantet: ndryshime gjatë shërbimit, pjata sezonale dhe klientë që nuk flasin të gjithë gjuhën tuaj.",
        Features = new[]
        {
            ("Në shumë gjuhë", "Shërbejini turistët në gjuhën e tyre. Ata e ndryshojnë me një prekje; ju e shkruani vetëm një herë."),
            ("Bërë për celular", "Menu të menduara për telefonin: të lexueshme edhe në dritë të ulët, të lehta me një dorë, dhe pasi hapen punojnë edhe kur bie Wi-Fi."),
            ("Përditësime të menjëhershme", "Mbaroi në 19:40? Një prekje. Klienti tjetër që hap menunë e sheh, dhe porositë nga tavolina për të nuk pranohen."),
            ("Me markën tuaj", "Ngjyrat, logoja dhe banneri juaj. Menuja duket si lokali juaj, jo si një program."),
            ("Menaxhim i thjeshtë", "Degët, kategoritë dhe pjatat në një panel. I rirendisni duke i tërhequr, pa pasur nevojë për trajnim."),
            ("I sigurt dhe i besueshëm", "Lidhje të enkriptuara, akses sipas rolit dhe degë të fshira që mund t'i riktheni. Menuja juaj është online sa herë që lokali është hapur.")
        },

        StepsTitle = "Tre hapa për të nisur",
        Steps = new[]
        {
            ("Krijoni degën tuaj", "Emri, adresa, logoja dhe banneri. Çdo lokal ka menunë, ngjyrat dhe linkun e vet.", "Një sallë bosh, gati për shërbim"),
            ("Shtoni menunë", "Kategoritë, pjatat, çmimet, fotot dhe alergjenët. E përktheni çdo pjatë një herë dhe çdo klient e lexon në gjuhën e vet.", "Një kuzhinier që grin erëza në një dërrasë"),
            ("Aktivizoni pjesën tjetër", "Printoni QR-në, publikoni faqen, hapni kalendarin e rezervimeve. Të njëjtat të dhëna, tri dyer të reja për klientët.", "Një pjatë e servirur në një tavolinë të shtruar")
        },

        PricingTitle = "Paguani vetëm për atë që përdorni",
        PricingText = "Çdo plan nis me menunë. Shtoni të tjerat për çdo degë dhe çaktivizojini kur s'ju duhen. {0} ditë falas, pa kartë.",
        SeePricing = "Shikoni çmimet",
        BuildOwnPlan = "Ndërtoni planin tuaj",
        CtaTitle = "Drejtoni gjithë lokalin nga një vend",
        CtaText = "Menu, faqe interneti, rezervime dhe menaxhim. Filloni me njërën dhe shtoni të tjerat kur të doni.",
        CtaTrial = "Provojeni falas për {0} ditë, pa kartë.",
        CtaContact = "Na tregoni për restorantin tuaj dhe ne jua hapim llogarinë."
    };
}
