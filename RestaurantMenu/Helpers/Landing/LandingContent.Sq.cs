namespace RestaurantMenu.Helpers.Landing;

// Albanian copy of the marketing content pages, written for Albanian restaurant owners (formal
// "ju", the words they use: menu dixhitale, degë, klientë, lekë). Same facts as
// LandingContent.En.cs; keep the two in step.
public static partial class LandingContent
{
    private static readonly Dictionary<string, Page> Sq = new()
    {
        ["digital-menu"] = new Page(
            Key: "digital-menu",
            Path: "/digital-menu",
            MetaTitle: "Menu dixhitale me QR për restorante · My Quick Menu",
            MetaDescription: "Menu dixhitale me QR në 7 gjuhë, me foto, 14 alergjenët e BE-së, filtra dietikë dhe porosi nga tavolina. Çmimi ndryshon kudo menjëherë. Nga {menu} në muaj.",
            H1: "Menu dixhitale me QR për restorante",
            Lede: "Menuja me QR është një menu dixhitale që klientët e hapin duke skanuar me kamerën e telefonit kodin në tavolinë, pa instaluar asnjë aplikacion. {definition}",
            Photo: "/img/guest-dining.webp",
            PhotoAlt: "Një kliente që lexon menunë në telefon në tavolinën e restorantit",
            Breadcrumb: "Menu dixhitale",
            Sections: new[]
            {
                new Section("Çfarë është menuja me QR?", new[]
                {
                    "Është menuja juaj si faqe interneti. Çdo tavolinë ka një kod QR të printuar; klienti drejton kamerën e telefonit nga kodi dhe menuja hapet në shfletues brenda një-dy sekondash. Pa shkarkuar asgjë dhe pa krijuar llogari.",
                    "Me My Quick Menu menuja mbahet në një vend të vetëm, në panelin tuaj. Kodet QR në tavolina, linku në biografinë e Instagram-it dhe faqja e internetit e restorantit tregojnë të njëjtën menu, ndaj çdo ndryshim që bëni e sheh menjëherë çdo klient."
                }, Id: "what"),
                new Section("Pse restorantet po lënë menutë e printuara?", new[]
                {
                    "Menuja e printuar vjetrohet sapo ndryshon një çmim ose mbaron një pjatë. Menuja me QR përditësohet brenda sekondash: ndryshoni çmimin në panel dhe klienti tjetër që hap menunë e sheh të riun. Shënojeni një pjatë si të mbaruar në 19:40 dhe ajo shfaqet menjëherë gri (ose fshihet, nëse e preferoni).",
                    "Turistët e lexojnë menunë në gjuhën e tyre. Ju e shkruani çdo pjatë një herë dhe shtoni përkthimet; klienti e ndryshon gjuhën me një prekje. My Quick Menu mbështet anglisht, shqip, italisht, gjermanisht, frëngjisht, spanjisht dhe turqisht, dhe formulari i pjatës mund t'ju sugjerojë përkthime për t'i kontrolluar [TODO: konfirmoni që Translation__GeminiApiKey është vendosur në prodhim, ose hiqeni këtë]."
                }, new[]
                {
                    "Foto për çdo pjatë, me pjata të veçuara dhe etiketa si Zgjedhja e kuzhinierit, E re ose Speciale e ditës",
                    "14 alergjenët e Rregullores (BE) 1169/2011 për çdo pjatë, si dhe etiketat vegane, vegjetariane, pa gluten, hallall dhe pikante, sipas të cilave klientët mund të filtrojnë",
                    "Përmasa dhe shtesa, që një pica të ketë dy madhësi me përbërës shtesë",
                    "Orare për çdo kategori, p.sh. mëngjesi deri në 11:00 ose happy hour gjatë javës, gri ose e fshehur në oraret e tjera",
                    "Çmimet në monedhën tuaj (20 monedha, mes tyre leku dhe euro)"
                }, Id: "why"),
                new Section("Sa kushton një menu me QR në Shqipëri?", new[]
                {
                    "Plani i menusë dixhitale kushton {menu} në muaj për çdo degë, me {trial} ditë provë falas dhe pa kartë për të filluar. Çmimet janë pa TVSH; TVSH-ja shtohet në faturë kur aplikohet.",
                    "Menuja është baza e çdo plani. Porositë nga tavolina me ekran kuzhine, faqja e internetit, rezervimet online dhe mjetet e menaxhimit janë module që i shtoni për çdo degë kur t'ju duhen. Shikoni [çmimet](/pricing) e plota."
                }, Id: "cost"),
                new Section("A mund të porosisin klientët nga tavolina?", new[]
                {
                    "Po, me modulin e porosive. Çdo tavolinë ka kodin e vet QR me kod tavoline, ndaj kuzhina e di saktë nga vjen porosia. Klientët shtojnë pjatat në listë dhe e dërgojnë porosinë; ajo shfaqet menjëherë në ekranin e kuzhinës dhe klienti e ndjek gjendjen në telefon. Kuzhina mund t'i ndalojë porositë kur ka shumë punë, dhe jashtë orarit tuaj porositë nuk pranohen."
                }, Id: "ordering"),
                new Section("A punon menuja pa internet?", new[]
                {
                    "Pasi klienti e ka hapur menunë, ajo vazhdon të punojë edhe kur bie Wi-Fi: telefoni mban një kopje dhe tregon kur është ruajtur. Klientët mund ta shtojnë edhe në ekranin kryesor si aplikacion, me logon dhe ngjyrat tuaja."
                }, Id: "offline")
            },
            FeaturesTitle: "Gjithçka që i duhet një menuje",
            Features: new[]
            {
                new Feature("ph-translate", "7 gjuhë", "Anglisht, shqip, italisht, gjermanisht, frëngjisht, spanjisht dhe turqisht. Klienti ndryshon me një prekje."),
                new Feature("ph-warning-circle", "14 alergjenët e BE-së", "Për çdo pjatë. Një pjatë pa deklarim nuk shfaqet kurrë si e sigurt."),
                new Feature("ph-funnel", "Filtra dietikë", "Vegane, vegjetariane, pa gluten, hallall dhe pikante, sipas zgjedhjes së klientit."),
                new Feature("ph-lightning", "Përditësim i menjëhershëm", "Çmimet, fotot dhe pjatat e mbaruara ndryshojnë në çdo tavolinë njëherësh."),
                new Feature("ph-palette", "Marka juaj", "Logoja, banneri dhe ngjyrat tuaja, të marra automatikisht nga logoja dhe të ndryshueshme."),
                new Feature("ph-chart-line-up", "Statistika të menusë", "Shikime, pjata të hapura dhe gjuhë të përdorura, pa cookies dhe pa të dhëna personale.")
            },
            StepsTitle: "Si funksionon",
            Steps: new[]
            {
                new Step("Krijoni degën", "Emri, adresa, logoja dhe banneri. Çdo degë ka menunë, ngjyrat dhe linkun e vet."),
                new Step("Shtoni pjatat", "Kategoritë, çmimet, fotot, alergjenët dhe përkthimet. I rirenditni duke i tërhequr."),
                new Step("Printoni kodet QR", "Kartonë tavoline (2 për fletë A4) ose ngjitëse (12 për fletë A4), një për çdo tavolinë ose një për gjithë sallën.")
            },
            Comparison: new Table(
                "Menu me QR, menu e printuar apo PDF?",
                "Si krahasohet menuja me QR e My Quick Menu me menunë e printuar dhe me një PDF pas kodit QR.",
                new[] { "", "My Quick Menu", "Menu e printuar", "Menu PDF" },
                new[]
                {
                    new[] { "Ndryshimi i një çmimi", "Në sekonda, kudo njëherësh", "Riprintim i çdo kopjeje", "Ndryshim i skedarit dhe ngarkim sërish" },
                    new[] { "Gjuhët", "7, sipas zgjedhjes së klientit", "Një për çdo printim", "Një për çdo skedar" },
                    new[] { "Alergjenët dhe filtrat dietikë", "Për çdo pjatë, me filtër", "Tekst i printuar", "Tekst në skedar" },
                    new[] { "Pjatat e mbaruara", "Me një prekje", "Nuk është e mundur", "Nuk është e mundur" },
                    new[] { "Lexohet mirë në telefon", "E ndërtuar për telefon", "Nuk aplikohet", "Me zmadhim dhe lëvizje" },
                    new[] { "Porosi nga tavolina", "Modul shtesë", "Jo", "Jo" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/menu.webp", 520, 1040, "Menuja dixhitale e një restoranti në telefon: banneri, kategoritë dhe pjatat me foto dhe çmime", "Menuja e një restoranti demonstrues, e hapur nga kodi QR i tavolinës."),
                new Screenshot("/img/screens/menu-dish.webp", 520, 1040, "Një pjatë e hapur në menunë dixhitale me foto, përshkrim, alergjenë dhe butonin për ta shtuar në listë", "Një pjatë me foton, alergjenët dhe etiketat dietike (restorant demonstrues).")
            },
            FaqTitle: "Pyetje për menutë me QR",
            Faqs: new[]
            {
                new Faq("A duhet të shkarkojnë klientët ndonjë aplikacion?", "Jo. Menuja hapet në shfletuesin e telefonit pasi skanohet kodi QR me kamerë. Nëse duan, klientët mund ta shtojnë në ekranin kryesor."),
                new Faq("Si i printoj kodet QR?", "Nga faqja e degës: zgjidhni kartonë tavoline (2 për fletë A4, që paloset në mes) ose ngjitëse (12 për fletë A4), me një kod QR për tavolinë ose një për gjithë restorantin, dhe printojini në çdo printer."),
                new Faq("Çfarë ndodh me kodet QR nëse ndryshoj emrin e restorantit?", "Vazhdojnë të punojnë. Kur emri i ri ndryshon linkun e menusë, linku i vjetër dhe çdo kod QR i printuar çojnë te linku i ri."),
                new Faq("Në cilat gjuhë mund të jetë menuja ime?", "Anglisht, shqip, italisht, gjermanisht, frëngjisht, spanjisht dhe turqisht, në çdo kombinim. Ju zgjidhni gjuhën kryesore me të cilën hapet linku; klientët mund të kalojnë te të tjerat."),
                new Faq("A del menuja offline nëse vonohet një pagesë?", "Jo. Menutë publike mbeten online edhe kur një faturë vonohet, sepse klientët i lexojnë në tavolinë. Funksionet e tjera ndalen derisa llogaria të rregullohet."),
                new Faq("A mund ta provoj para se të paguaj?", "Po. Çdo plan fillon me {trial} ditë provë falas dhe pa kartë.")
            },
            CtaTitle: "Vendosni menunë tuaj në çdo tavolinë që sot",
            CtaText: "Filloni me menunë dixhitale dhe shtoni faqen, rezervimet ose porositë kur të doni."),

        ["restaurant-website"] = new Page(
            Key: "restaurant-website",
            Path: "/restaurant-website",
            MetaTitle: "Faqe interneti për restorant · My Quick Menu",
            MetaDescription: "Faqe interneti e shpejtë për restorantin, e ndërtuar nga menuja, orari dhe fotot tuaja, me rezervim online, domainin tuaj dhe të dhëna gati për Google.",
            H1: "Faqe interneti për restorant",
            Lede: "Faqja e internetit nga My Quick Menu ndërtohet nga menuja, fotot dhe orari që i mbani tashmë në panel, ndaj nuk vjetrohet kurrë. {definition}",
            Photo: "/img/cafe-interior.webp",
            PhotoAlt: "Një kafene e ndriçuar me tavolina druri",
            Breadcrumb: "Faqe interneti",
            Sections: new[]
            {
                new Section("Çfarë përfshin faqja e internetit?", new[]
                {
                    "Emrin, foton e bannerit dhe ngjyrat e restorantit; një moto me një rresht dhe një tekst rreth jush në secilën nga gjuhët tuaja; pjatat e veçuara me foto dhe çmime; orarin me gjendjen \"Hapur tani\"; adresën me hartë; dhe butonat për të parë menunë e plotë dhe për të rezervuar tavolinë.",
                    "Meqë lexon të njëjtat të dhëna si menuja juaj me QR, kur ndryshoni një çmim, një foto ose orarin në panel, ndryshon edhe faqja. Nuk ka asgjë për të sinkronizuar."
                }, Id: "includes"),
                new Section("A mund ta përdor domainin tim?", new[]
                {
                    "Po. Çdo faqe ka një adresë falas në myquickmenu.com, dhe me opsionin e domainit mund të punojë në adresën tuaj, p.sh. www.restoranti-juaj.al. Shtoni një regjistrim DNS te regjistruesi i domainit; aplikacioni e kontrollon dhe certifikata e sigurt https vendoset automatikisht. Adresa e vjetër më pas çon te domaini juaj, që linket dhe renditja në Google të mblidhen në një adresë të vetme."
                }, Id: "domain"),
                new Section("A do ta gjejë Google faqen e restorantit tim?", new[]
                {
                    "Faqja ndërtohet që të gjendet. Çdo faqe ka titullin dhe përshkrimin e vet në secilën gjuhë, të dhëna të strukturuara që i tregojnë Google-it orarin, adresën, kuzhinën, menunë dhe linkun e rezervimit, një sitemap me të gjitha gjuhët, dhe faqe të shpejta që nuk varen nga skripte të rënda.",
                    "Si shfaqeni e kontrolloni ju te faqja Search & sharing e degës: titulli dhe përshkrimi juaj për Google me pamje paraprake të drejtpërdrejtë, lloji i lokalit dhe kuzhina, qyteti dhe vendndodhja në hartë, linku i profilit tuaj në Google Business, dhe imazhi që shfaqet kur linku ndahet në WhatsApp ose Facebook."
                }, Id: "google"),
                new Section("Sa kushton një faqe interneti për restorant?", new[]
                {
                    "Faqja është modul mbi menunë dixhitale: {website} në muaj për çdo degë, dhe {domain} në muaj për çdo domain tuajin. Plani i gatshëm Menu, faqe interneti dhe rezervime kushton {guests} në muaj për çdo degë. Çdo plan fillon me {trial} ditë provë falas. Shikoni [çmimet](/pricing) e plota."
                }, Id: "cost")
            },
            FeaturesTitle: "E ndërtuar për restorante, jo për dizajnerë",
            Features: new[]
            {
                new Feature("ph-arrows-clockwise", "Gjithmonë e përditësuar", "Menuja, çmimet, fotot dhe orari vijnë drejt nga paneli juaj."),
                new Feature("ph-translate", "Në shumë gjuhë", "Të njëjtat gjuhë si menuja, me adresën e vet për secilën."),
                new Feature("ph-calendar-check", "Butoni i rezervimit", "Klientët rezervojnë pa dalë nga faqja, kur rezervimet janë aktive."),
                new Feature("ph-globe-simple", "Domaini juaj", "Adresa juaj me certifikatë https automatike."),
                new Feature("ph-magnifying-glass", "Gati për Google", "Tituj, përshkrime, sitemap dhe të dhëna të strukturuara, automatikisht."),
                new Feature("ph-instagram-logo", "Rrjetet sociale", "Instagram dhe Facebook në faqe, të lidhur me restorantin tuaj për Google.")
            },
            StepsTitle: "Nga menuja te faqja e internetit",
            Steps: new[]
            {
                new Step("Plotësoni degën", "Emri, adresa, banneri, logoja, orari dhe menuja: shumica e restoranteve i kanë tashmë."),
                new Step("Shkruani një moto dhe disa rreshta për veten", "Në secilën nga gjuhët tuaja, te faqja Website."),
                new Step("Publikojeni", "Faqja del online në adresën falas; lidhni domainin tuaj kur të doni.")
            },
            Comparison: new Table(
                "Faqe nga My Quick Menu apo faqe e ndërtuar me porosi?",
                "Si krahasohet faqja e internetit nga My Quick Menu me një faqe të ndërtuar nga një agjenci dhe me të pasurit vetëm rrjete sociale.",
                new[] { "", "Faqe nga My Quick Menu", "Faqe me porosi", "Vetëm rrjete sociale" },
                new[]
                {
                    new[] { "Menuja dhe çmimet të përditësuara", "Automatikisht", "Dikush duhet ta ndryshojë", "Postime dhe foto" },
                    new[] { "Rezervim tavoline online", "I përfshirë", "Shtojcë ose shërbim më vete", "Mesazhe" },
                    new[] { "Disa gjuhë", "Po, si menuja", "Punë shtesë", "Një për postim" },
                    new[] { "Domaini juaj", "Po", "Po", "Jo" },
                    new[] { "Të dhëna të strukturuara për Google", "Automatike", "Varet nga zhvilluesi", "Jo" },
                    new[] { "Kostoja", "Nga {website} në muaj për degë", "Kosto dizajni dhe zhvillimi", "Falas, por nuk është e juaja" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/site.webp", 1280, 800, "Faqja e internetit e një restoranti e bërë me My Quick Menu: foto banneri, emri, motoja, gjendja e orarit dhe butonat për menunë dhe rezervimin", "Faqja e një restoranti demonstrues, e ndërtuar nga paneli i tij.")
            },
            FaqTitle: "Pyetje për faqet e restoranteve",
            Faqs: new[]
            {
                new Faq("A më duhet dizajner apo programues?", "Jo. Faqja ndërtohet nga të dhënat e degës dhe menuja. Ju shkruani motonë dhe tekstin rreth jush, zgjidhni cilat seksione shfaqen dhe e publikoni."),
                new Faq("A mund ta mbaj domainin që kam?", "Po. Drejtojeni domainin te My Quick Menu me një regjistrim DNS (faqja Website ju tregon saktë cilin) dhe faqja punon aty, me https të vendosur automatikisht."),
                new Faq("Çfarë ndodh me faqen nëse ndryshoj një çmim?", "Ndryshon në faqe në të njëjtin moment si në menunë me QR, sepse të dyja lexojnë të njëjtat të dhëna."),
                new Faq("A është faqja në shqip dhe anglisht?", "Po, dhe në cilëndo nga 7 gjuhët që ka menuja juaj. Çdo gjuhë ka adresën e vet, që Google të tregojë atë të duhurën."),
                new Faq("A mund të rezervojnë klientët nga faqja?", "Po, kur moduli i rezervimeve është aktiv: butoni \"Rezervo një tavolinë\" hap faqen tuaj të rezervimeve.")
            },
            CtaTitle: "Jepini restorantit një faqe që përditësohet vetë",
            CtaText: "Filloni me menunë; faqja e internetit është një klikim larg."),

        ["table-bookings"] = new Page(
            Key: "table-bookings",
            Path: "/table-bookings",
            MetaTitle: "Rezervim tavoline online për restorante · My Quick Menu",
            MetaDescription: "Rezervime online për restorante: klientët zgjedhin një orë të lirë nga menuja, faqja ose Instagram-i, marrin konfirmim dhe kujtesë. Provë falas.",
            H1: "Rezervim tavoline online",
            Lede: "Sistemi i rezervimeve online u lejon klientëve ta rezervojnë vetë tavolinën, në çdo orë, duke zgjedhur një orar të lirë në një faqe në vend që të telefonojnë. {definition}",
            Photo: "/img/terrace.webp",
            PhotoAlt: "Një tarracë restoranti buzë detit, gati për darkë",
            Breadcrumb: "Rezervime",
            Sections: new[]
            {
                new Section("Si funksionojnë rezervimet online?", new[]
                {
                    "Restoranti juaj ka një faqe rezervimesh me emrin, logon dhe ngjyrat tuaja. Klienti zgjedh ditën, numrin e personave dhe një orë të lirë; ofrohen vetëm oraret që përputhen me orarin e punës dhe me kapacitetin tuaj. Lë emrin dhe numrin e telefonit dhe rezervimi bëhet. Faqja e rezervimeve ka linkun e vet, ndaj mund ta vendosni në faqen e internetit, në menu, në biografinë e Instagram-it ose ta dërgoni në WhatsApp.",
                    "Rregullat i vendosni ju: sa zgjat një orar (30 minuta si fillim), grupi më i madh që mund të rezervojë online (8 persona si fillim), sa ditë përpara mund të rezervohet (60 ditë si fillim), ditët kur rezervimi online është i mbyllur, dhe nëse rezervimet konfirmohen automatikisht apo nga ju."
                }, Id: "how"),
                new Section("A marrin klientët konfirmim dhe kujtesë?", new[]
                {
                    "Po. Klienti e sheh konfirmimin menjëherë në ekran dhe e merr me email, si dhe me SMS kur SMS-ja është aktive [TODO: konfirmoni nëse SMS-ja është aktive në prodhim]. Një kujtesë dërgohet disa orë para rezervimit (3 orë si fillim). Çdo rezervim ka linkun e vet, ku klienti mund ta shohë, ta shtojë në kalendar ose ta anulojë, dhe tavolina lirohet për dikë tjetër."
                }, Id: "reminders"),
                new Section("Si i menaxhoj rezervimet e ditës?", new[]
                {
                    "Faqja e rezervimeve ju tregon ditën me një shikim: kush vjen, në çfarë ore dhe sa veta. Shënoni klientët si të ulur kur mbërrijnë ose si të paparaqitur kur nuk vijnë, shtoni rezervimet që vijnë me telefon, dhe konfirmoni ose refuzoni kërkesat nëse i miratoni vetë rezervimet. Për të ndaluar rezervimet false, një numër telefoni mund të mbajë deri në tre rezervime të ardhshme."
                }, Id: "manage"),
                new Section("Sa kushton një sistem rezervimesh për tavolina?", new[]
                {
                    "Rezervimet online janë modul mbi menunë dixhitale: {bookings} në muaj për çdo degë. Plani Menu, faqe interneti dhe rezervime kushton {guests} në muaj për çdo degë. Nuk ka tarifë për rezervim ose për klient. Çdo plan fillon me {trial} ditë provë falas. Shikoni [çmimet](/pricing) e plota."
                }, Id: "cost")
            },
            FeaturesTitle: "Rezervime pa telefonata",
            Features: new[]
            {
                new Feature("ph-clock", "Vetëm orare vërtet të lira", "Oraret ndjekin orarin e punës dhe sa klientë mund të uleni."),
                new Feature("ph-link", "Rezervim nga kudo", "Nga menuja, faqja e internetit, Instagram-i ose një mesazh WhatsApp."),
                new Feature("ph-bell-ringing", "Kujtesa", "Një kujtesë para çdo rezervimi, me email dhe SMS kur është aktive."),
                new Feature("ph-user-minus", "Klientët që nuk vijnë", "Shënoni kush u ul dhe kush nuk erdhi, dhe shikoni kush vjen rregullisht."),
                new Feature("ph-check-circle", "Konfirmim automatik ose manual", "Konfirmoni çdo rezervim vetë ose lërini të konfirmohen automatikisht."),
                new Feature("ph-translate", "Në gjuhën e klientit", "Faqja e rezervimit dhe mesazhet ndjekin gjuhët e menusë suaj.")
            },
            StepsTitle: "Filloni të merrni rezervime",
            Steps: new[]
            {
                new Step("Vendosni orarin e punës", "Rezervimet e ndjekin, edhe për shërbimet që zgjasin pas mesnatës."),
                new Step("Zgjidhni rregullat", "Kohëzgjatja e orarit, madhësia e grupit, sa ditë përpara, ditët e mbyllura, konfirmimi."),
                new Step("Ndani linkun e rezervimeve", "Shfaqet automatikisht në menu dhe në faqe; shtojeni edhe në Instagram.")
            },
            Comparison: new Table(
                "Rezervime online apo telefon dhe fletore?",
                "Si krahasohen rezervimet online me My Quick Menu me marrjen e rezervimeve me telefon.",
                new[] { "", "Rezervime me My Quick Menu", "Telefon dhe fletore" },
                new[]
                {
                    new[] { "Klientët rezervojnë edhe natën", "Po, në çdo orë", "Vetëm kur dikush përgjigjet" },
                    new[] { "Rezervime të dyfishta", "Të parandaluara nga oraret e lira", "Ndodhin lehtë" },
                    new[] { "Konfirmim dhe kujtesë", "Automatike", "Telefonata ose mesazhe me dorë" },
                    new[] { "Klienti mund ta anulojë", "Nga linku i rezervimit", "Vetëm duke telefonuar" },
                    new[] { "Historiku i mosparaqitjeve", "I ruajtur për çdo rezervim", "Në letër" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/booking.webp", 520, 1040, "Faqja e rezervimit online të një restoranti në telefon: data, numri i personave dhe oraret e lira për të zgjedhur", "Faqja e rezervimit që shohin klientët (restorant demonstrues).")
            },
            FaqTitle: "Pyetje për rezervimet online",
            Faqs: new[]
            {
                new Faq("A ka tarifë për çdo rezervim?", "Jo. Rezervimet përfshihen në çmimin mujor të modulit të rezervimeve për degë, sado klientë të rezervojnë."),
                new Faq("A mund t'i miratoj vetë rezervimet?", "Po. Çaktivizoni konfirmimin automatik dhe çdo rezervim pret që ju ta konfirmoni ose ta refuzoni; klienti njoftohet në të dyja rastet."),
                new Faq("A mund ta anulojnë klientët?", "Po, nga linku në konfirmim. Tavolina lirohet sërish për klientët e tjerë."),
                new Faq("A mund t'i ndaloj rezervimet online në disa ditë?", "Po. Shtoni ditë të mbyllura, si festat ose eventet private, dhe ato ditë nuk ofrohen online."),
                new Faq("Nga ku mund të rezervojnë klientët?", "Nga linku i faqes së rezervimeve, nga butoni \"Rezervo një tavolinë\" në menunë me QR dhe në faqen e internetit, dhe nga kudo ku e ndani linkun.")
            },
            CtaTitle: "Lërini klientët të rezervojnë ndërsa ju gatuani",
            CtaText: "Aktivizoni rezervimet online për restorantin tuaj në pak minuta."),

        ["restaurant-management"] = new Page(
            Key: "restaurant-management",
            Path: "/restaurant-management",
            MetaTitle: "Program menaxhimi për restorante · My Quick Menu",
            MetaDescription: "Ekran kuzhine për porositë nga tavolina, magazinë me receta, turne stafi dhe shitje ditore për çdo degë, në të njëjtin panel me menunë me QR. Provë falas.",
            H1: "Program menaxhimi për restorante",
            Lede: "Programi i menaxhimit të restorantit e mban gjithë punën e prapaskenës në një vend: porositë për kuzhinën, magazinën, turnet e stafit dhe shitjet e ditës. {definition}",
            Photo: "/img/chef-plating.webp",
            PhotoAlt: "Një kuzhinier që përfundon pjatat nën dritat e kuzhinës",
            Breadcrumb: "Menaxhimi",
            Sections: new[]
            {
                new Section("Çfarë është ekrani i kuzhinës?", new[]
                {
                    "Ekrani i kuzhinës është një ekran në kuzhinë që tregon porositë sapo vijnë, në vend të fletëve të letrës. Me My Quick Menu, porositë që klientët dërgojnë nga tavolina shfaqen aty menjëherë, me numrin e tavolinës, pjatat, përmasat dhe shënimet. Kuzhina e kalon çdo porosi nëpër fazat e saj dhe klienti e sheh gjendjen në telefon. Kuzhina mund t'i ndalojë edhe porositë e reja kur ka shumë punë. Ekrani i kuzhinës vjen me modulin e porosive nga tavolina."
                }, Id: "kds"),
                new Section("Si funksionon menaxhimi i magazinës?", new[]
                {
                    "Shtoni përbërësit me njësinë, sasinë aktuale, koston dhe një nivel minimal që ju paralajmëron para se të mbarojnë. Çdo pjate i vendosni recetën; kur bëhet një porosi nga tavolina, përbërësit e saj zbriten automatikisht nga magazina dhe kthehen nëse porosia anulohet. Furnizimet, humbjet dhe inventarët regjistrohen si lëvizje, secila me kush e bëri dhe pse, ndaj historiku e shpjegon gjithmonë sasinë aktuale."
                }, Id: "stock"),
                new Section("A mund të planifikoj turnet dhe të shoh shitjet ditore?", new[]
                {
                    "Po. Planifikoni turnet e stafit për çdo degë dhe mbyllni çdo ditë me shitjet e saj: porositë nga tavolina mblidhen për çdo ditë pune sipas orës tuaj, dhe menaxheri shënon paratë në dorë dhe me kartë në mbyllje. Çdo natë dita ruhet, ndaj ditët e kaluara janë gjithmonë aty për krahasim."
                }, Id: "shifts"),
                new Section("A funksionon për më shumë se një degë?", new[]
                {
                    "Po. Çdo degë ka menunë, magazinën, turnet, shitjet dhe stafin e vet, dhe pamja e përgjithshme i tregon të gjitha degët bashkë. Ftoni stafin me rolin e duhur: menaxherët drejtojnë gjithë degën dhe menunë, ndërsa redaktorët (p.sh. kamarierët) mund të ndryshojnë pjatat dhe t'i shënojnë si të mbaruara."
                }, Id: "branches"),
                new Section("Sa kushton një program menaxhimi për restorante?", new[]
                {
                    "Menaxhimi (magazina, recetat, turnet dhe shitjet) kushton {management} në muaj për çdo degë, dhe porositë nga tavolina me ekranin e kuzhinës {ordering} në muaj për çdo degë, të dyja mbi menunë dixhitale. Plani Të gjitha kushton {everything} në muaj për çdo degë. Çdo plan fillon me {trial} ditë provë falas. Shikoni [çmimet](/pricing) e plota."
                }, Id: "cost")
            },
            FeaturesTitle: "Prapaskena, e lidhur me menunë",
            Features: new[]
            {
                new Feature("ph-monitor", "Ekrani i kuzhinës", "Porositë nga tavolina vijnë menjëherë, me shënime, përmasa dhe shtesa."),
                new Feature("ph-package", "Magazina dhe recetat", "Përbërësit zbriten automatikisht kur porositen pjatat."),
                new Feature("ph-warning", "Niveli minimal", "Shikoni çfarë po mbaron para shërbimit, jo gjatë tij."),
                new Feature("ph-users-three", "Turnet dhe rolet", "Planifikoni turnet dhe jepini secilit aksesin që i duhet."),
                new Feature("ph-receipt", "Shitjet e ditës", "Porositë për çdo ditë pune, me numërimin e parave dhe kartës."),
                new Feature("ph-buildings", "Pamje për të gjitha degët", "Çdo degë krah për krah në një panel.")
            },
            StepsTitle: "Si të filloni",
            Steps: new[]
            {
                new Step("Aktivizoni modulet", "Porositë nga tavolina për ekranin e kuzhinës, Menaxhimi për magazinën, turnet dhe shitjet."),
                new Step("Shtoni përbërësit dhe recetat", "Filloni me pjatat që shiten më shumë; shtoni të tjerat me kohë."),
                new Step("Ftoni stafin", "Jepini menaxherëve dhe kamarierëve hyrjen dhe rolin e tyre.")
            },
            Comparison: new Table(
                "Çfarë përfshin çdo modul",
                "Funksionet e menaxhimit në My Quick Menu dhe moduli ku bën pjesë secili.",
                new[] { "Funksioni", "Moduli", "Çmimi për degë" },
                new[]
                {
                    new[] { "Porosi nga tavolina përmes menusë me QR", "Porositë", "{ordering} në muaj" },
                    new[] { "Ekrani i kuzhinës", "Porositë", "{ordering} në muaj" },
                    new[] { "Magazina, recetat dhe niveli minimal", "Menaxhimi", "{management} në muaj" },
                    new[] { "Turnet e stafit", "Menaxhimi", "{management} në muaj" },
                    new[] { "Shitjet e ditës dhe numërimi i arkës", "Menaxhimi", "{management} në muaj" },
                    new[] { "Rolet e stafit dhe pamja e degëve", "Çdo plan", "E përfshirë" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/kitchen.webp", 1280, 800, "Ekrani i kuzhinës që tregon porositë e tavolinave si karta me pjatat, shënimet dhe gjendjen", "Ekrani i kuzhinës me porositë e tavolinave në kohë reale (restorant demonstrues).")
            },
            FaqTitle: "Pyetje për menaxhimin e restorantit",
            Faqs: new[]
            {
                new Faq("A më duhet pajisje e veçantë për ekranin e kuzhinës?", "Jo. Punon në shfletuesin e çdo tableti, kompjuteri ose televizori me shfletues, të lidhur me internet."),
                new Faq("A zbritet magazina automatikisht?", "Po, për pjatat që kanë recetë: çdo porosi nga tavolina zbret përbërësit nga magazina, dhe një porosi e anuluar i kthen."),
                new Faq("A e shohin stafi gjithçka?", "Jo. Çdo anëtar i stafit ka një rol: redaktorët ndryshojnë pjatat dhe disponueshmërinë, menaxherët drejtojnë gjithë degën, dhe vetëm pronari menaxhon stafin dhe pagesat."),
                new Faq("A mund ta përdor menaxhimin pa porositë nga tavolina?", "Po. Çdo modul aktivizohet më vete për çdo degë; ekrani i kuzhinës kërkon porositë nga tavolina, sepse tregon pikërisht ato porosi.")
            },
            CtaTitle: "Drejtoni kuzhinën, magazinën dhe stafin nga një vend",
            CtaText: "Shtoni modulet që ju duhen te menuja, degë pas dege."),

        ["faq"] = new Page(
            Key: "faq",
            Path: "/faq",
            MetaTitle: "Pyetje të shpeshta: menu QR dhe rezervime · My Quick Menu",
            MetaDescription: "Përgjigje për My Quick Menu: sa kushton, gjuhët, alergjenët, kodet QR, faqet e internetit, rezervimet online, pagesat, prova falas dhe ndihma në Shqipëri.",
            H1: "Pyetje të shpeshta",
            Lede: "{definition} Më poshtë janë pyetjet që na bëjnë më shpesh pronarët e restoranteve, me përgjigje të shkurtra dhe të qarta.",
            Photo: "/img/dining-room.webp",
            PhotoAlt: "Një sallë bosh, gati për shërbim",
            Breadcrumb: "Pyetje të shpeshta",
            Sections: Array.Empty<Section>(),
            FaqTitle: "Të përgjithshme",
            Faqs: new[]
            {
                new Faq("Çfarë është My Quick Menu?", "{definition}"),
                new Faq("Sa kushton My Quick Menu?", "Menuja dixhitale kushton {menu} në muaj për degë. Plani Menu, faqe interneti dhe rezervime kushton {guests} në muaj dhe plani Të gjitha {everything} në muaj për degë. Çmimet janë pa TVSH; TVSH-ja shtohet në faturë kur aplikohet."),
                new Faq("A ka provë falas?", "Po: {trial} ditë për çdo plan, pa kartë."),
                new Faq("Për kë është My Quick Menu?", "Për restorante, kafene, bare, furra dhe lokale të tjera që shërbejnë ushqim dhe pije, nga një kafene e vetme te restorantet me disa degë."),
                new Faq("A është My Quick Menu në shqip?", "Po. Kjo faqe, regjistrimi dhe faqet e klientëve (menuja, faqja e internetit dhe rezervimi) janë në shqip, dhe menutë e klientëve mund të jenë në 7 gjuhë. Paneli i restorantit tani për tani është në anglisht."),
                new Faq("Si paguaj?", "Me transfertë bankare kundrejt faturës, çdo muaj ose çdo vit [TODO: shtoni pagesën me kartë kur të jetë aktive]."),
                new Faq("A mund ta anuloj?", "[TODO: shkruani afatin e njoftimit dhe si anulohet, si në kushtet e përdorimit.]"),
                new Faq("Kush qëndron pas My Quick Menu?", "My Quick Menu ndërtohet dhe drejtohet nga 4CS, një kompani softueri në Tiranë. Shikoni [rreth nesh](/about)."),
                new Faq("Si marr ndihmë?", "Shkruani në info@myquickmenu.com ose telefononi +355 69 633 7906. Shikoni [kontakt](/contact) [TODO: orari i ndihmës].")
            },
            PricingTeaser: false,
            CtaTitle: "Keni ende një pyetje?",
            CtaText: "Na shkruani dhe ju përgjigjemi brenda ditës [TODO: konfirmoni kohën e përgjigjes që mund të premtoni]."),

        ["about"] = new Page(
            Key: "about",
            Path: "/about",
            MetaTitle: "Rreth nesh · My Quick Menu nga 4CS, Tiranë",
            MetaDescription: "My Quick Menu është platformë për restorante e bërë në Tiranë nga 4CS: menu me QR, faqe interneti, rezervime online dhe menaxhim në një panel të vetëm.",
            H1: "Rreth nesh",
            Lede: "{definition}",
            Photo: "/img/evening-room.webp",
            PhotoAlt: "Salla e një restoranti në mbrëmje",
            Breadcrumb: "Rreth nesh",
            Sections: new[]
            {
                new Section("Kush e bën My Quick Menu?", new[]
                {
                    "My Quick Menu ndërtohet dhe drejtohet nga [4CS](https://4cs.al), një kompani softueri në Tiranë. Produktin e projektojmë, e ndërtojmë dhe e mbështesim vetë, ndaj kur diçka duhet rregulluar ose përmirësuar, e bën ekipi ynë.",
                    "[TODO: disa fjali me fjalët tuaja: kur dhe pse 4CS nisi My Quick Menu, dhe kush është në ekip.]"
                }, Id: "who"),
                new Section("Pse e ndërtuam", new[]
                {
                    "Restorantet ndryshojnë gjithë kohës: çmimet, pjatat, orari, stafi në turn. Shumica e programeve e trajtojnë menunë, faqen e internetit, rezervimet dhe menaxhimin si gjëra të ndara që duhen mbajtur në rregull më vete. Ne ndërtuam një platformë ku i shkruani të gjitha një herë dhe janë të sakta kudo: në tavolinë, në faqen tuaj, në Google dhe në kuzhinë.",
                    "Nisëm me atë që u duhet më shumë restoranteve në Shqipëri: menu në disa gjuhë për turistët, çmime në lekë ose euro, alergjenët e BE-së, dhe rezervime që nuk varen nga dikush që t'i përgjigjet telefonit."
                }, Id: "why"),
                new Section("Çfarë bën My Quick Menu", new[]
                {
                    "Katër produkte që ndajnë të njëjtat të dhëna: [menu dixhitale me QR](/digital-menu), [faqe interneti për restorant](/restaurant-website), [rezervim tavoline online](/table-bookings) dhe [mjete menaxhimi](/restaurant-management) (ekran kuzhine, magazinë, turne dhe shitje). Restorantet fillojnë me menunë dhe shtojnë të tjerat për çdo degë kur u duhen. Shikoni [çmimet](/pricing)."
                }, Id: "what"),
                new Section("Ku na gjeni", new[]
                {
                    "My Quick Menu, Tiranë, Shqipëri. Email info@myquickmenu.com, telefon +355 69 633 7906. Më shumë te [faqja e kontaktit](/contact)."
                }, Id: "where")
            },
            PricingTeaser: false),

        ["contact"] = new Page(
            Key: "contact",
            Path: "/contact",
            MetaTitle: "Kontakt · My Quick Menu, Tiranë",
            MetaDescription: "Kontaktoni My Quick Menu në Tiranë: shkruani në info@myquickmenu.com ose telefononi +355 69 633 7906 për menu me QR, faqe interneti dhe rezervime online.",
            H1: "Kontakt",
            Lede: "Keni pyetje për menunë, faqen e internetit, rezervimet ose llogarinë tuaj? Na shkruani ose na telefononi; përgjigjemi në shqip dhe anglisht.",
            Photo: "/img/fine-dining.webp",
            PhotoAlt: "Një pjatë e servirur në një tavolinë të shtruar",
            Breadcrumb: "Kontakt",
            Sections: new[]
            {
                new Section("Si mund ta kontaktoj My Quick Menu?", new[]
                {
                    "Email: [info@myquickmenu.com](mailto:info@myquickmenu.com)",
                    "Telefon: [+355 69 633 7906](tel:+355696337906)",
                    "Adresa: Tiranë, Shqipëri [TODO: adresa e rrugës, nëse doni ta bëni publike]",
                    "[TODO: orari i ndihmës, p.sh. e hënë deri të shtunë 9:00-18:00, dhe numri WhatsApp nëse e përdorni.]"
                }, Id: "reach"),
                new Section("Kush qëndron pas My Quick Menu?", new[]
                {
                    "My Quick Menu bëhet nga [4CS](https://4cs.al), një kompani softueri në Tiranë. Lexoni më shumë [rreth nesh](/about) ose [pyetjet e shpeshta](/faq)."
                }, Id: "company")
            },
            PricingTeaser: false)
    };
}
