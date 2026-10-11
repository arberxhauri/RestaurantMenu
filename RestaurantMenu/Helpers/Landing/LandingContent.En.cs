namespace RestaurantMenu.Helpers.Landing;

// English copy of the marketing content pages. Facts checked against the product on 2026-10-11
// (see LandingContent for the writing rules and tokens). Keep it in step with LandingContent.Sq.cs.
public static partial class LandingContent
{
    private static readonly Dictionary<string, Page> En = new()
    {
        ["digital-menu"] = new Page(
            Key: "digital-menu",
            Path: "/digital-menu",
            MetaTitle: "QR Code Menu for Restaurants · My Quick Menu",
            MetaDescription: "QR digital menu in 7 languages with photos, the 14 EU allergens, diet filters and table ordering. One change updates every table. From {menu}/month.",
            H1: "QR code menu for restaurants",
            Lede: "A QR code menu is a digital menu that guests open by scanning a code on the table with their phone camera, with no app to install. {definition}",
            Photo: "/img/guest-dining.webp",
            PhotoAlt: "A guest reading the menu on her phone at a restaurant table",
            Breadcrumb: "Digital menu",
            Sections: new[]
            {
                new Section("What is a QR code menu?", new[]
                {
                    "It is your menu as a web page. Each table gets a printed QR code; a guest points their phone camera at it and the menu opens in the browser in a second or two. Nothing to download, no account to create.",
                    "With My Quick Menu the menu lives in one place, your dashboard. The QR codes on the tables, the link in your Instagram bio and your restaurant website all show that same menu, so a change you make is the change every guest sees."
                }, Id: "what"),
                new Section("Why restaurants switch from printed menus", new[]
                {
                    "A printed menu is out of date the moment a price changes or a dish runs out. A QR menu is updated in seconds: change the price in the dashboard and the next guest who opens the menu sees it. Mark a dish as sold out at 19:40 and it is greyed out (or hidden, if you prefer) straight away.",
                    "Tourists read the menu in their own language. You write each dish once and add translations; guests switch language with one tap. My Quick Menu supports English, Albanian, Italian, German, French, Spanish and Turkish, and the dish form can suggest translations for you to check [TODO: confirm Translation__GeminiApiKey is set in production, or remove this]."
                }, new[]
                {
                    "Photos of every dish, with featured dishes and badges such as Chef's pick, New or Today's special",
                    "The 14 allergens of EU Regulation 1169/2011 on every dish, plus vegan, vegetarian, gluten-free, halal and spicy tags guests can filter by",
                    "Sizes and add-ons, so a pizza can come in two sizes with extra toppings",
                    "Serving times per category, for example breakfast until 11:00 or a weekday happy hour, greyed out or hidden at other times",
                    "Prices in your own currency (20 currencies, including the Albanian lek and the euro)"
                }, Id: "why"),
                new Section("How much does a QR menu cost in Albania?", new[]
                {
                    "The digital menu plan costs {menu} a month per branch, with a {trial}-day free trial and no card needed to start. Prices are shown without VAT; VAT is added on the invoice where it applies.",
                    "The menu is the base of every plan. Table ordering with a kitchen display, a restaurant website, online bookings and management tools are optional modules you can add per branch whenever you need them. See the full [pricing](/pricing)."
                }, Id: "cost"),
                new Section("Can guests order from the table?", new[]
                {
                    "Yes, with the ordering module. Every table has its own QR code with a table code, so the kitchen knows exactly where an order comes from. Guests add dishes to their list and send the order; it appears instantly on the kitchen display, and the guest can follow its status on their phone. The kitchen can pause orders during a rush, and orders are not accepted outside your opening hours."
                }, Id: "ordering"),
                new Section("Does the menu work without internet?", new[]
                {
                    "Once a guest has opened your menu, it keeps working if the Wi-Fi drops: the phone keeps a copy and says when it was saved. Guests can also add it to their home screen like an app, with your logo and colours."
                }, Id: "offline")
            },
            FeaturesTitle: "Everything a menu needs",
            Features: new[]
            {
                new Feature("ph-translate", "7 languages", "English, Albanian, Italian, German, French, Spanish and Turkish. Guests switch in one tap."),
                new Feature("ph-warning-circle", "14 EU allergens", "Declared per dish. A dish without a declaration is never shown as safe."),
                new Feature("ph-funnel", "Diet filters", "Vegan, vegetarian, gluten-free, halal and spicy, filterable by guests."),
                new Feature("ph-lightning", "Instant updates", "Prices, photos and sold-out dishes change on every table at once."),
                new Feature("ph-palette", "Your brand", "Your logo, banner and colours, taken from your logo automatically and adjustable."),
                new Feature("ph-chart-line-up", "Menu insights", "Views, opened dishes and languages used, without cookies or personal data.")
            },
            StepsTitle: "How it works",
            Steps: new[]
            {
                new Step("Create your branch", "Name, address, logo and banner. Each branch gets its own menu, colours and link."),
                new Step("Add your dishes", "Categories, prices, photos, allergens and translations. Reorder them by dragging."),
                new Step("Print your QR codes", "Table tents (2 per A4 sheet) or stickers (12 per A4), one per table or one for the whole room.")
            },
            Comparison: new Table(
                "QR menu, printed menu or PDF?",
                "How a My Quick Menu QR menu compares with a printed menu and a PDF behind a QR code.",
                new[] { "", "My Quick Menu", "Printed menu", "PDF menu" },
                new[]
                {
                    new[] { "Change a price", "Seconds, everywhere at once", "Reprint every copy", "Edit the file and upload it again" },
                    new[] { "Languages", "7, chosen by the guest", "One per print run", "One per file" },
                    new[] { "Allergens and diet filters", "Per dish, filterable", "Printed text", "Text in the file" },
                    new[] { "Sold-out dishes", "One tap", "Not possible", "Not possible" },
                    new[] { "Readable on a phone", "Built for phones", "Not applicable", "Zoom and scroll" },
                    new[] { "Ordering from the table", "Optional module", "No", "No" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/menu.webp", 520, 1040, "A restaurant's digital menu on a phone: the restaurant banner, categories and dishes with photos and prices", "The guest menu of a demo restaurant, opened from the table's QR code."),
                new Screenshot("/img/screens/menu-dish.webp", 520, 1040, "A dish opened on the digital menu with its photo, description, allergens and an add to list button", "A dish with its photo, allergens and diet tags (demo restaurant).")
            },
            FaqTitle: "Questions about QR menus",
            Faqs: new[]
            {
                new Faq("Do guests need to download an app?", "No. The menu opens in the phone's browser after scanning the QR code with the camera. Guests can optionally add it to their home screen."),
                new Faq("How do I print the QR codes?", "From your branch page: choose table tents (2 per A4 sheet, folded in half) or stickers (12 per A4), with one QR code per table or one for the whole restaurant, and print them on any printer."),
                new Faq("What happens to my QR codes if I rename the restaurant?", "They keep working. When a new name changes the menu link, the old link and every printed QR code redirect to the new one."),
                new Faq("Which languages can my menu have?", "English, Albanian, Italian, German, French, Spanish and Turkish, in any combination. You choose the main language your link opens in; guests can switch to the others."),
                new Faq("Is my menu taken offline if a payment is late?", "No. Public menus stay online even if an invoice is late, because guests are reading them at the table. Other features pause until the account is up to date."),
                new Faq("Can I try it before paying?", "Yes. Every plan starts with a {trial}-day free trial and no card is needed.")
            },
            CtaTitle: "Put your menu on every table today",
            CtaText: "Start with the digital menu and add a website, bookings or ordering whenever you want."),

        ["restaurant-website"] = new Page(
            Key: "restaurant-website",
            Path: "/restaurant-website",
            MetaTitle: "Website Builder for Restaurants · My Quick Menu",
            MetaDescription: "A fast restaurant website built from your menu, hours and photos, with online booking, your own domain and Google-ready data. Always up to date. Free trial.",
            H1: "Website builder for restaurants",
            Lede: "A restaurant website from My Quick Menu is built from the menu, photos and opening hours you already keep in the dashboard, so it is never out of date. {definition}",
            Photo: "/img/cafe-interior.webp",
            PhotoAlt: "A bright café dining room with wooden tables",
            Breadcrumb: "Restaurant website",
            Sections: new[]
            {
                new Section("What does the website include?", new[]
                {
                    "Your restaurant's name, banner photo and colours; a one-line tagline and an about text in each of your languages; your featured dishes with photos and prices; opening hours with an \"Open now\" status; your address with a map; and buttons to see the full menu and to book a table.",
                    "Because it reads from the same data as your QR menu, changing a price, a photo or your hours in the dashboard changes the website too. There is nothing to keep in sync."
                }, Id: "includes"),
                new Section("Can I use my own domain?", new[]
                {
                    "Yes. Every website gets a free address on myquickmenu.com, and with the own-domain option it can run on your own address, such as www.yourrestaurant.al. You add one DNS record at your domain registrar; the app checks it and the secure https certificate is set up for you. The old address then redirects to your domain, so links and search ranking build up on one address."
                }, Id: "domain"),
                new Section("Will Google find my restaurant website?", new[]
                {
                    "The website is built to be found. Every page has its own title and description in each language, structured data that tells Google your opening hours, address, cuisine, menu and booking link, a sitemap listing every language, and fast pages that work without heavy scripts.",
                    "You control how you appear on the Search & sharing page of your branch: your own search title and description with a live Google preview, the type of place and cuisine, your city and map location, your Google Business Profile link, and the image shown when your link is shared on WhatsApp or Facebook."
                }, Id: "google"),
                new Section("How much does a restaurant website cost?", new[]
                {
                    "The website is a module on top of the digital menu: {website} a month per branch, and {domain} a month for each own domain. The ready-made Menu, website and bookings plan is {guests} a month per branch. Every plan starts with a {trial}-day free trial. See the full [pricing](/pricing)."
                }, Id: "cost")
            },
            FeaturesTitle: "Built for restaurants, not for designers",
            Features: new[]
            {
                new Feature("ph-arrows-clockwise", "Always up to date", "Menu, prices, photos and hours come straight from your dashboard."),
                new Feature("ph-translate", "Multilingual", "The same languages as your menu, with its own address for each."),
                new Feature("ph-calendar-check", "Booking button", "Guests book a table without leaving the site, when bookings are on."),
                new Feature("ph-globe-simple", "Own domain", "Your own address with an automatic https certificate."),
                new Feature("ph-magnifying-glass", "Ready for Google", "Titles, descriptions, sitemap and structured data, automatically."),
                new Feature("ph-instagram-logo", "Social links", "Instagram and Facebook on the site, and linked to your restaurant for Google.")
            },
            StepsTitle: "From menu to website",
            Steps: new[]
            {
                new Step("Fill in your branch", "Name, address, banner, logo, opening hours and menu: most restaurants have these already."),
                new Step("Write a tagline and a few lines about you", "In each of your languages, on the Website page."),
                new Step("Publish", "The site goes live on its free address; connect your own domain whenever you like.")
            },
            Comparison: new Table(
                "My Quick Menu website or a custom-built site?",
                "How a My Quick Menu restaurant website compares with a site built by an agency and with only having social media pages.",
                new[] { "", "My Quick Menu website", "Custom-built site", "Social media only" },
                new[]
                {
                    new[] { "Menu and prices up to date", "Automatically", "Someone has to edit it", "Posts and photos" },
                    new[] { "Online table booking", "Built in", "Extra plugin or service", "Messages" },
                    new[] { "Several languages", "Yes, same as your menu", "Extra work", "One per post" },
                    new[] { "Own domain", "Yes", "Yes", "No" },
                    new[] { "Structured data for Google", "Automatic", "Depends on the developer", "No" },
                    new[] { "Cost", "From {website} a month per branch", "Design and development fees", "Free, but you don't own it" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/site.webp", 1280, 800, "A restaurant website made with My Quick Menu: banner photo, restaurant name, tagline, opening status and buttons to see the menu and book a table", "A demo restaurant's website, built from its dashboard.")
            },
            FaqTitle: "Questions about restaurant websites",
            Faqs: new[]
            {
                new Faq("Do I need a designer or developer?", "No. The website is built from your branch details and menu. You write a tagline and an about text, choose which sections to show, and publish."),
                new Faq("Can I keep my existing domain?", "Yes. Point your domain to My Quick Menu with one DNS record (the Website page shows exactly which) and the site runs on it, with https set up automatically."),
                new Faq("What happens to my website if I change a price?", "It changes on the website at the same moment as on your QR menu, because both read the same data."),
                new Faq("Is the website available in Albanian and English?", "Yes, and in any of the 7 languages your menu offers. Each language has its own address so Google can show the right one."),
                new Faq("Can guests book from the website?", "Yes, when the bookings module is on: a \"Book a table\" button opens your booking page.")
            },
            CtaTitle: "Give your restaurant a website that keeps itself up to date",
            CtaText: "Start with your menu; the website is one switch away."),

        ["table-bookings"] = new Page(
            Key: "table-bookings",
            Path: "/table-bookings",
            MetaTitle: "Online Table Reservation System · My Quick Menu",
            MetaDescription: "Online table bookings for restaurants: guests pick a free time from your menu, website or Instagram, get a confirmation and a reminder. Free trial.",
            H1: "Online table reservation system",
            Lede: "An online table reservation system lets guests book a table themselves, at any hour, by picking a free time on a web page instead of calling. {definition}",
            Photo: "/img/terrace.webp",
            PhotoAlt: "A seaside restaurant terrace set for dinner",
            Breadcrumb: "Table bookings",
            Sections: new[]
            {
                new Section("How do online table bookings work?", new[]
                {
                    "Your restaurant gets a booking page with your name, logo and colours. A guest chooses the day, the number of people and a free time; only times that fit your opening hours and your capacity are offered. They leave a name and phone number and the booking is made. Your booking page has its own link, so you can put it on your website, on your menu, in your Instagram bio or send it on WhatsApp.",
                    "You decide the rules: how long a time slot is (30 minutes by default), the largest party that can book online (8 by default), how many days ahead guests can book (60 by default), dates when online booking is closed, and whether bookings are confirmed automatically or by you."
                }, Id: "how"),
                new Section("Do guests get a confirmation and a reminder?", new[]
                {
                    "Yes. The guest sees the confirmation on screen straight away and receives it by email, and by SMS where SMS is enabled [TODO: confirm SMS is switched on in production]. A reminder goes out a few hours before the booking (3 hours by default). Every booking has its own link where the guest can see it, add it to their calendar or cancel it, which frees the table for someone else."
                }, Id: "reminders"),
                new Section("How do I manage the day's bookings?", new[]
                {
                    "The bookings page shows the day at a glance: who is coming, when and how many. Mark guests as seated when they arrive or as a no-show when they don't, add bookings that come in by phone, and confirm or decline requests if you approve bookings yourself. To stop fake bookings, one phone number can hold at most three upcoming bookings."
                }, Id: "manage"),
                new Section("How much does a table booking system cost?", new[]
                {
                    "Online bookings are a module on top of the digital menu: {bookings} a month per branch. The Menu, website and bookings plan is {guests} a month per branch. There is no fee per booking or per guest. Every plan starts with a {trial}-day free trial. See the full [pricing](/pricing)."
                }, Id: "cost")
            },
            FeaturesTitle: "Bookings without the phone calls",
            Features: new[]
            {
                new Feature("ph-clock", "Only real free times", "Slots follow your opening hours and how many guests you can seat."),
                new Feature("ph-link", "Book from anywhere", "From your menu, your website, Instagram or a WhatsApp message."),
                new Feature("ph-bell-ringing", "Reminders", "A reminder before every booking, by email and SMS where enabled."),
                new Feature("ph-user-minus", "No-show tracking", "Mark guests as seated or no-show and see who usually comes."),
                new Feature("ph-check-circle", "Auto or manual confirm", "Confirm every booking yourself or let them confirm automatically."),
                new Feature("ph-translate", "In the guest's language", "The booking page and messages follow your menu's languages.")
            },
            StepsTitle: "Start taking bookings",
            Steps: new[]
            {
                new Step("Set your opening hours", "Bookings follow them, including services that run past midnight."),
                new Step("Choose your rules", "Slot length, party size, how far ahead, closed dates, confirmation."),
                new Step("Share your booking link", "It appears on your menu and website automatically; add it to Instagram too.")
            },
            Comparison: new Table(
                "Online bookings or phone and notebook?",
                "How online table booking with My Quick Menu compares with taking bookings by phone.",
                new[] { "", "My Quick Menu bookings", "Phone and notebook" },
                new[]
                {
                    new[] { "Guests can book at night", "Yes, any time", "Only when someone answers" },
                    new[] { "Double bookings", "Prevented by the free times", "Easy to make" },
                    new[] { "Confirmation and reminder", "Automatic", "Manual calls or messages" },
                    new[] { "Guest can cancel", "From their booking link", "Only by calling" },
                    new[] { "No-show history", "Kept per booking", "On paper" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/booking.webp", 520, 1040, "A restaurant's online booking page on a phone: the date, number of guests and the free times to choose from", "The booking page guests see (demo restaurant).")
            },
            FaqTitle: "Questions about online bookings",
            Faqs: new[]
            {
                new Faq("Is there a fee per booking?", "No. Bookings are part of the bookings module's monthly price per branch, however many guests book."),
                new Faq("Can I approve bookings myself?", "Yes. Turn off automatic confirmation and each booking waits for you to confirm or decline it; the guest is told either way."),
                new Faq("Can guests cancel?", "Yes, from the link in their confirmation. The table becomes free again for other guests."),
                new Faq("Can I stop online bookings on certain days?", "Yes. Add closed dates, such as holidays or private events, and those days are not offered online."),
                new Faq("Where can guests book from?", "From your booking page link, the \"Book a table\" button on your QR menu and on your website, and anywhere you share the link.")
            },
            CtaTitle: "Let guests book while you cook",
            CtaText: "Turn on online bookings for your restaurant in a few minutes."),

        ["restaurant-management"] = new Page(
            Key: "restaurant-management",
            Path: "/restaurant-management",
            MetaTitle: "Restaurant Management Software · My Quick Menu",
            MetaDescription: "Kitchen display for table orders, stock with recipes, staff shifts and end-of-day sales for every branch, in the same dashboard as your QR menu. Free trial.",
            H1: "Restaurant management software",
            Lede: "Restaurant management software keeps the back office in one place: orders to the kitchen, stock, staff shifts and the day's sales. {definition}",
            Photo: "/img/chef-plating.webp",
            PhotoAlt: "A chef finishing plates under the kitchen pass lights",
            Breadcrumb: "Restaurant management",
            Sections: new[]
            {
                new Section("What is a kitchen display system?", new[]
                {
                    "A kitchen display is a screen in the kitchen that shows orders as they arrive, instead of paper tickets. With My Quick Menu, orders guests send from the table appear on it live, with the table number, dishes, sizes and notes. The kitchen moves each order through its stages and the guest sees the status on their phone. The kitchen can also pause new orders during a rush. The kitchen display comes with the table ordering module."
                }, Id: "kds"),
                new Section("How does stock management work?", new[]
                {
                    "List your ingredients with their unit, current level, cost and a low level that warns you before you run out. Give each dish a recipe; when a table order is placed, its ingredients come off the stock automatically, and go back if the order is cancelled. Deliveries, waste and counts are recorded as movements, each with who made it and why, so the history always explains the current level."
                }, Id: "stock"),
                new Section("Can I plan staff shifts and see daily sales?", new[]
                {
                    "Yes. Plan your team's shifts per branch, and close each day with its sales: table orders are added up per business day in your time zone, and the manager records the cash and card counted at closing. Every night the day is saved, so past days are always there to compare."
                }, Id: "shifts"),
                new Section("Does it work for more than one branch?", new[]
                {
                    "Yes. Each branch has its own menu, stock, shifts, sales and team, and the overview shows every branch together. Invite your team with the role that fits: managers can run the whole branch and its menu, while editors (for example waiters) can change dishes and mark them sold out."
                }, Id: "branches"),
                new Section("How much does restaurant management software cost?", new[]
                {
                    "Management (stock, recipes, shifts and sales) is {management} a month per branch, and table ordering with the kitchen display is {ordering} a month per branch, both on top of the digital menu. The Everything plan is {everything} a month per branch. Every plan starts with a {trial}-day free trial. See the full [pricing](/pricing)."
                }, Id: "cost")
            },
            FeaturesTitle: "The back office, connected to the menu",
            Features: new[]
            {
                new Feature("ph-monitor", "Kitchen display", "Table orders arrive live, with notes, sizes and add-ons."),
                new Feature("ph-package", "Stock and recipes", "Ingredients come off automatically as dishes are ordered."),
                new Feature("ph-warning", "Low-stock levels", "See what is running low before service, not during it."),
                new Feature("ph-users-three", "Shifts and team roles", "Plan shifts and give each person the access they need."),
                new Feature("ph-receipt", "End-of-day sales", "Orders per business day, with the cash and card count."),
                new Feature("ph-buildings", "Multi-branch overview", "Every branch side by side in one dashboard.")
            },
            StepsTitle: "Getting started",
            Steps: new[]
            {
                new Step("Switch on the modules", "Table ordering for the kitchen display, Management for stock, shifts and sales."),
                new Step("Add ingredients and recipes", "Start with your best-selling dishes; add the rest over time."),
                new Step("Invite your team", "Give managers and waiters their own sign-in and role.")
            },
            Comparison: new Table(
                "What each module includes",
                "The management features of My Quick Menu and the module each one belongs to.",
                new[] { "Feature", "Module", "Price per branch" },
                new[]
                {
                    new[] { "Table ordering from the QR menu", "Ordering", "{ordering} a month" },
                    new[] { "Kitchen display", "Ordering", "{ordering} a month" },
                    new[] { "Stock, recipes and low-stock levels", "Management", "{management} a month" },
                    new[] { "Staff shifts", "Management", "{management} a month" },
                    new[] { "End-of-day sales and cash count", "Management", "{management} a month" },
                    new[] { "Team roles and multi-branch overview", "Every plan", "Included" }
                }),
            Screenshots: new[]
            {
                new Screenshot("/img/screens/kitchen.webp", 1280, 800, "The kitchen display showing table orders as cards with their dishes, notes and status", "The kitchen display with live table orders (demo restaurant).")
            },
            FaqTitle: "Questions about restaurant management",
            Faqs: new[]
            {
                new Faq("Do I need special hardware for the kitchen display?", "No. It runs in the web browser of any tablet, computer or TV with a browser, connected to the internet."),
                new Faq("Does stock go down automatically?", "Yes, for dishes with a recipe: each table order takes its ingredients off the stock, and a cancelled order puts them back."),
                new Faq("Can staff see everything?", "No. Each team member has a role: editors can change dishes and availability, managers can run the whole branch, and only the owner manages the team and billing."),
                new Faq("Can I use management without table ordering?", "Yes. Each module can be switched on per branch on its own; the kitchen display needs table ordering because it shows those orders.")
            },
            CtaTitle: "Run the kitchen, the stock and the team from one place",
            CtaText: "Add the modules you need to your menu, branch by branch."),

        ["faq"] = new Page(
            Key: "faq",
            Path: "/faq",
            MetaTitle: "FAQ: QR Menus, Websites & Bookings · My Quick Menu",
            MetaDescription: "Answers about My Quick Menu: what it costs, languages, allergens, QR codes, websites, online bookings, payments, the free trial and support in Albania.",
            H1: "Frequently asked questions",
            Lede: "{definition} Below are the questions restaurant owners ask us most, with short, direct answers.",
            Photo: "/img/dining-room.webp",
            PhotoAlt: "An empty dining room ready for service",
            Breadcrumb: "FAQ",
            Sections: Array.Empty<Section>(),
            FaqTitle: "General",
            Faqs: new[]
            {
                new Faq("What is My Quick Menu?", "{definition}"),
                new Faq("How much does My Quick Menu cost?", "The digital menu is {menu} a month per branch. The Menu, website and bookings plan is {guests} a month and the Everything plan {everything} a month per branch. Prices are without VAT; VAT is added on the invoice where it applies."),
                new Faq("Is there a free trial?", "Yes: {trial} days on any plan, with no card needed."),
                new Faq("Who is My Quick Menu for?", "Restaurants, cafés, bars, bakeries and other places that serve food and drink, from a single café to restaurants with several branches."),
                new Faq("Is My Quick Menu available in Albanian?", "Yes. This website, signup and the guest pages (menu, website and booking) are in Albanian, and guest menus can be in 7 languages. The restaurant dashboard is in English for now."),
                new Faq("How do I pay?", "By bank transfer against an invoice, monthly or yearly [TODO: add card payment once it is live]."),
                new Faq("Can I cancel?", "[TODO: state the notice period and how to cancel, as in the terms of use.]"),
                new Faq("Who is behind My Quick Menu?", "My Quick Menu is built and run by 4CS, a software company in Tirana, Albania. See [about us](/about)."),
                new Faq("How do I get help?", "Email info@myquickmenu.com or call +355 69 633 7906. See [contact](/contact) [TODO: support hours].")
            },
            PricingTeaser: false,
            CtaTitle: "Still have a question?",
            CtaText: "Write to us and we'll answer the same day [TODO: confirm the response time you can promise]."),

        ["about"] = new Page(
            Key: "about",
            Path: "/about",
            MetaTitle: "About My Quick Menu · Made in Tirana by 4CS",
            MetaDescription: "My Quick Menu is a restaurant platform made in Tirana, Albania by 4CS: QR menus, restaurant websites, online bookings and management in one dashboard.",
            H1: "About My Quick Menu",
            Lede: "{definition}",
            Photo: "/img/evening-room.webp",
            PhotoAlt: "A restaurant dining room in the evening",
            Breadcrumb: "About",
            Sections: new[]
            {
                new Section("Who makes My Quick Menu?", new[]
                {
                    "My Quick Menu is built and run by [4CS](https://4cs.al), a software company in Tirana, Albania. We design, build and support the product ourselves, so when something needs fixing or improving, it is our own team that does it.",
                    "[TODO: a few sentences in your own words: when and why 4CS started My Quick Menu, and who is on the team.]"
                }, Id: "who"),
                new Section("Why we built it", new[]
                {
                    "Restaurants change all the time: prices, dishes, opening hours, the staff on shift. Most software treats the menu, the website, bookings and the back office as separate things to keep in sync. We built one platform where you enter everything once and it is right everywhere: on the table, on your website, in Google and in your kitchen.",
                    "We started with what restaurants in Albania need most: menus in several languages for tourists, prices in lek or euro, the EU allergens, and bookings that don't depend on someone picking up the phone."
                }, Id: "why"),
                new Section("What My Quick Menu does", new[]
                {
                    "Four products that share the same data: a [QR digital menu](/digital-menu), a [restaurant website](/restaurant-website), [online table bookings](/table-bookings) and [management tools](/restaurant-management) (kitchen display, stock, shifts and sales). Restaurants start with the menu and add the rest per branch when they need it. See [pricing](/pricing)."
                }, Id: "what"),
                new Section("Where to find us", new[]
                {
                    "My Quick Menu, Tirana, Albania. Email info@myquickmenu.com, phone +355 69 633 7906. More on the [contact page](/contact)."
                }, Id: "where")
            },
            PricingTeaser: false),

        ["contact"] = new Page(
            Key: "contact",
            Path: "/contact",
            MetaTitle: "Contact My Quick Menu · Tirana, Albania",
            MetaDescription: "Contact My Quick Menu in Tirana: email info@myquickmenu.com or call +355 69 633 7906 about QR menus, restaurant websites and online bookings.",
            H1: "Contact",
            Lede: "Questions about the menu, a website, bookings or your account? Write or call us; we answer in Albanian and English.",
            Photo: "/img/fine-dining.webp",
            PhotoAlt: "A plated dish on a set table",
            Breadcrumb: "Contact",
            Sections: new[]
            {
                new Section("How can I reach My Quick Menu?", new[]
                {
                    "Email: [info@myquickmenu.com](mailto:info@myquickmenu.com)",
                    "Phone: [+355 69 633 7906](tel:+355696337906)",
                    "Address: Tirana, Albania [TODO: street address, if you want it public]",
                    "[TODO: support hours, e.g. Monday to Saturday 9:00-18:00, and a WhatsApp number if you use one.]"
                }, Id: "reach"),
                new Section("Who is behind My Quick Menu?", new[]
                {
                    "My Quick Menu is made by [4CS](https://4cs.al), a software company in Tirana. Read more [about us](/about) or the [frequently asked questions](/faq)."
                }, Id: "company")
            },
            PricingTeaser: false)
    };
}
