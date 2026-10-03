# My Quick Menu: what to build next, and how

Written after the October 2026 redesign. Each item says **why** it matters, **what** changes in the data model, and **where** it goes in this codebase (ASP.NET Core 8 MVC, EF Core + Postgres, Razor views, `wwwroot/js`).

Ordered by value per effort. Items 1 to 6 are small and make promises the landing page already makes true.

---

## 0. Fix first: done in code, deploy steps left for you

All seven issues found during the redesign are fixed and tested end to end (22 browser checks plus a simulated production upgrade).

| Issue | Status |
|---|---|
| Production DB password committed in `appsettings.json` | Removed from the file. Production reads `ConnectionStrings__DefaultConnection`; local runs use `appsettings.Development.json` (local `mqm_dev`). **You still must rotate the password**, because it remains in git history from commit `fefc839`. |
| Default admin `Admin@123` | No password in code. `ADMIN_INITIAL_PASSWORD` env var, or a random one printed once to the server log. An existing admin still on `Admin@123` gets a new random password at startup (printed to the log) and must change it at next sign-in. "Must change password" is now enforced on every back-office page. |
| SQL Server migrations on Postgres | Replaced by `InitialPostgres` (baseline matching production) + `SoftDeleteMenus`. `docs/migrations/prod-upgrade.sql` brings production in line; it is idempotent and was tested twice on a production-like copy. |
| Admin could delete themselves / listed as owner | Owners list shows owners only. Admins can't be removed from that page (also refused server-side). Removing an owner now also signs them out and takes their menus offline, as the confirm dialog said. |
| New owner's password shown on screen | No password is created. The owner gets an invite link (3 days, single use) to set their own: emailed when SMTP is configured, otherwise shown once to the admin with a Copy button. "Invite pending" status and "New invite link" action on the owners list. |
| Blank 404 for unknown menu links | Friendly pages for 404 ("This menu isn't available" for menu links) and 403. |
| Permanent deletes | Branches, categories and dishes are soft-deleted (branches never were, despite the `IsDeleted` column). Every delete shows **Undo**. Dish photos stay on disk so Undo is complete. A deleted branch no longer blocks its name. |

Also fixed while doing this: admins were sent to the owner dashboard (access denied) after changing their password; the access-denied page didn't exist; sign-in keys weren't persisted, so every Render deploy signed everyone out (now stored on the persistent disk under `keys/`).

### Deploy checklist

1. **Render, Postgres:** rotate the database password. Take a backup.
2. **Render, web service, Environment:**
   - `ConnectionStrings__DefaultConnection` = the new connection string (Npgsql format, as before)
   - optional `ADMIN_INITIAL_PASSWORD` (only used if the admin account doesn't exist yet)
   - optional email: `Smtp__Host`, `Smtp__Port` (587), `Smtp__User`, `Smtp__Password`, `Smtp__From`
   - optional translate-assist: `Translation__GeminiApiKey` (free key from https://aistudio.google.com/apikey)
3. **Before deploying**, run `psql "<external connection string>" -v ON_ERROR_STOP=1 -f docs/migrations/prod-upgrade.sql`.
4. Deploy. If the admin still used `Admin@123`, find the replacement password in the Render logs ("The admin still used the old default password"), sign in, and choose a new one.

**Migrations run automatically on deploy** (`Services/DatabaseMigrator.cs`, called from `Program.cs` before anything is served). On startup the app takes a Postgres lock, records the Postgres baseline if the database predates it (what step 1 of `prod-upgrade.sql` does), and applies any pending migrations. If a migration fails, the app doesn't start and Render keeps the previous version running. So a schema change is just `dotnet ef migrations add <Name>`, commit and deploy. Still take a Render backup before deploys that change the schema.

Keep migrations **additive** (new columns with defaults, new tables). During a zero-downtime deploy the old version keeps serving for a moment against the new schema, and it breaks if a column it reads was renamed or dropped. Rename or drop in two deploys: first stop using the column, then remove it. To manage migrations by hand instead, set `Database__AutoMigrate=false` and run `dotnet ef migrations script <PreviousMigration> --idempotent` on production before deploying.

---

## 1. Sold out / available toggle: done

**Why:** the landing page sells "Sold out at 19:40? Hide it." This is the most-used mid-service action.

- **Model:** `Product.IsAvailable` (default true, existing dishes stay available) and `Branch.HideSoldOut` (default false). Migration `AddProductAvailability`.
- **Endpoint:** `ProductController.ToggleAvailability(id, isAvailable?)`, antiforgery-protected, same ownership check as `Edit`. It takes the state to set (a missing value flips it), returns `{ id, isAvailable }` to fetch, and redirects with a flash message for a plain form post, so it works without JavaScript.
- **Back office:** a switch on each dish row in Branch Details (`site.js`: the switch flips at once, goes back if the save fails, and only the newest tap counts), a "Sold out" chip, a greyed-out row and an "N sold out" counter. Branch Edit has a "Hide sold-out dishes from the menu" switch.
- **Menu:** sold-out dishes are greyed out with a translated "Sold out" chip and have no `+`; the dish sheet shows the chip instead of "Add to list". With `HideSoldOut` they are left out of the page and the JSON-LD (a section whose dishes are all sold out disappears too). The guest's saved list flags sold-out dishes and leaves them out of the estimated total. JSON-LD offers carry `availability` InStock or SoldOut.
- **Deploy:** nothing manual. The migration is applied automatically at startup (see section 0). `docs/migrations/2026-10-03-add-product-availability.sql` is kept for manual use with `Database__AutoMigrate=false`.
- **Possible follow-ups:** "Mark all available" for the start of the next service; live refresh of open guest menus (SignalR, item 14).

## 2. QR codes and printable table tents: done

**Why:** every restaurant needs this on day one, and it's what the guest actually scans.

- **No schema change.** The table travels on the link as `/menu/{slug}?t=14` (1 to 9999). `SeoService.MenuUrl(name, lang, table)` builds every menu link; canonical and hreflang URLs never carry `t`, so table links don't create duplicate search results.
- **Server:** NuGet `QRCoder` (pure .NET, works in the Linux container) behind `Services/QrCodeService`. `BranchController.Qr(id, table?, format=svg|png, download?)` (owner only) returns the code; `BranchController.Print(id, layout, from, to, noTables, copies, lang, headline)` renders the print page with the SVGs inline, so nothing has to load before printing.
- **Print page** (`Views/Branch/Print.cshtml`, `wwwroot/css/print.css`): table tents (two per A4, each a strip folded in half with the top face upside down so both sides read upright) or stickers (12 per A4 with cut guides). Tables N to M (up to 200 per print), or a whole-menu code without a table number. The card language and headline can be changed; each card has the logo, name, headline, the QR code in the brand colour frame, "Table 14" and the typed address. All options are in the URL, so a setup can be bookmarked. PNG and SVG downloads are there for print shops.
- **Entry points:** "QR & print" in the Branch Details header and in the branch card menu on the dashboard.
- **Menu:** `MenuController` reads `t` (invalid values are ignored) and keeps it through the canonical 301 and the language switcher. The menu shows a "Table 14" chip under the name and the table at the top of "Your list", translated (`Helpers/TableText`).
- **Note:** codes encode `Seo:BaseUrl` when it is set, otherwise the host the owner is using. Set `Seo__BaseUrl` before printing if a custom domain is planned, so printed codes never point at the onrender.com address.

## 3. Structured allergens and dietary tags: done

**Why:** EU Regulation 1169/2011 requires the 14 allergens to be available; guests filter by vegan, gluten-free and similar.

- **Model** (`Models/Dietary.cs`): `[Flags] enum Allergen` (the 14 Annex II allergens) and `[Flags] enum Diet` (vegan, vegetarian, spicy, gluten-free, halal), stored as ints on `Product`. Bit values are persisted, so never renumber them. `Product.Allergens` is **nullable**: null = not declared yet, `None` = declared as containing none. Migration `AddAllergensAndDiets` leaves existing dishes undeclared.
- **Server:** `ProductController` binds `int[] allergenFlags`, `bool allergensNone`, `int[] dietFlags`, ORs them, ignores unknown bits, makes vegan imply vegetarian, and rejects contradictions (vegan with milk/eggs/fish/crustaceans/molluscs, vegetarian with fish/seafood, gluten-free with gluten, "none" plus allergens). A form that fails validation now keeps its typed translations; before, they came back empty and a second save wiped them.
- **Back office:** "Allergens & dietary tags" section in `_ProductForm.cshtml` (checkbox chips, an explicit "Contains none of the 14", live status). The free-text field is now just "Nutrition". Branch Details counts dishes without allergen info and flags each one with a link to its form.
- **Menu:** diet tags and "Contains: …" on each card, an Allergens block in the dish sheet (declared / none / "not provided, ask your server"), and a filter sheet next to search: "Show only" diets (all must match) and "Hide dishes containing" allergens. **Undeclared dishes are hidden while an allergen is selected**, and the sheet says so. Live count, badge, a summary bar with Clear, works together with search, remembered per menu in the guest's browser. The filter button only appears once the restaurant has declared something. All labels are in the 7 menu languages (`Helpers/DietaryText.cs`; have native speakers review them).
- **SEO:** JSON-LD `suitableForDiet` (Vegan, Vegetarian, GlutenFree, Halal diets).

## 4. Drag to reorder dishes: done

- **Endpoint:** `ProductController.UpdateOrder([FromBody] { categoryId, productIds })`, antiforgery-protected, ownership-checked, one query for the category's dishes. `DisplayOrder` becomes the position. The list must be exactly the category's current dishes (no missing, duplicate, deleted or foreign ids), otherwise **409** with "reload the page", so a stale tab can't scramble the order.
- **UI:** a drag handle on every dish row in Branch Details; each category's dishes reorder on their own (no dragging between categories; change the category in the dish form). The Sortable block in `site.js` is now generic (`data-sortable`, `-item`, `-key`, `-extra`, `-state`) and drives categories too. New for both: **keyboard reordering** (focus a handle, Arrow Up / Down; moves are announced and batched into one save), **revert to the last saved order** if a save fails, and only the newest save's result counts.
- **Ordering fixes:** dishes are ordered by `DisplayOrder` then `Id` everywhere (back office, guest menu, JSON-LD); most existing dishes share `DisplayOrder = 0`, so the order was undefined and could differ between pages. The "Order in category" number field is gone; new dishes go to the end of their category, editing keeps the position, and moving a dish to another category puts it at the end of that category.

## 5. Menu analytics: done

**Why:** owners renew when they can see that guests use the menu.

- **Model:** `MenuEvent { BranchId, ProductId?, Type (View, DishOpen, AddToList, LanguageSwitch), Lang, CreatedUtc }`, migration `AddMenuEvents`. No IP, user agent, cookie or visitor id is stored, so nothing in it is personal data. No foreign keys, so events outlive deleted dishes and an insert never fails because of the menu's state.
- **Capture** (`Services/MenuAnalytics`): `MenuController.Index` records a View when the page is actually served to a guest (not for the canonical 301). `POST /menu/event` takes `navigator.sendBeacon` posts from `menu.js` for dish opens, adds to the list (not removals) and language switches. It only stores events that make sense for that menu: a known type, a live dish of that branch, a language the branch offers. Not counted: bots, crawlers, link previews (WhatsApp, Facebook…), headless browsers and scripts, prefetches, and anyone signed in (owners checking their menu). The endpoint is rate-limited to 60 posts per minute per client address, which is held in memory only. Recording failures are logged and never break the menu.
- **Reports** (`Services/MenuInsights`): days are counted in `Analytics:TimeZone` (default `Europe/Tirane`), grouped in Postgres. **Branch → Insights** (`/branch/insights/{id}?days=7|30|90`): stat tiles with change vs the previous period, a server-rendered "menu views per day" column chart (hover and keyboard tooltips, table view, no JS or chart library needed), most opened dishes (including removed ones) and the share of each menu language. **Dashboard:** "Last 7 days" tiles across all branches and a "N views this week" chip on each branch card. Chart colour `--chart-1` was validated with the dataviz palette checks in light and dark mode.
- **Retention:** `AnalyticsRetentionService` deletes events older than `Analytics:RetentionDays` (default 400) daily.
- **Privacy page** updated to say exactly what is counted and what isn't.
- **Later:** a nightly `MenuDaily` roll-up when volume grows (the reports would read it for whole past days); a "busiest hours" chart; QR-table scans (`?t=`) as a dimension.

## 6. Translate-assist: done (free)

**Why:** translations are the slowest part of setup, and the multilingual menu is a headline feature.

- **Provider: Google Gemini API, free tier** (key from Google AI Studio, no billing account). Chosen over DeepL (its free API plan no longer takes new sign-ups and needs a card) and the Claude API (paid). An LLM translates menus with context: dish names like Tiramisu or fior di latte stay as they are, units and numbers are kept, and the wording reads like a menu. Note: on the free tier Google may use submitted text to improve its products. Only dish and category text that is published on the public menu anyway is sent, never anything about guests or owners.
- **Setup (Render, Environment):** `Translation__GeminiApiKey` = the AI Studio key. Optional `Translation__Models` (default `gemini-3.8-flash,gemini-3.5-flash-lite`): models are tried in order when one is rate-limited, retired or down, so a model being shut down only needs this variable changed. Without a key the button simply doesn't appear.
- **Server:** `Services/TranslationService` calls `generateContent` with a JSON response schema (one object per target language, exactly the requested fields). Menu text is passed as JSON data, separate from the instructions. Replies are validated (missing or oversized values are dropped) and failures come back as plain messages: quota used up, invalid key, blocked, incomplete reply. `TranslateController.Fill` checks the branch belongs to the owner, accepts only the branch's own languages and known fields (dish: name, description, nutrition; category: name), max 1000 characters per field, and is limited to 20 requests per minute per owner. Nothing is saved there.
- **Screens:** "Fill translations" above the language tabs in `_ProductForm.cshtml` and `_CategoryTranslations.cshtml` (shared `_TranslateBar` partial). It fills only empty fields; if some are filled, the owner chooses to replace them or keep them. Suggestions are highlighted until edited, the language tabs tick, and the owner saves as usual.
- **Fixed alongside:** the category form lost typed translations when saving failed validation (the dish form was fixed in item 3).
- **Not done here:** moving the guest UI strings to `.resx`. That depends on a native-speaker review of the wording; today the strings live in one place per feature (`ui` in `Views/Menu/Index.cshtml`, `Helpers/DietaryText.cs`, `Helpers/TableText.cs`).

## 7. Opening hours and "Open now": done

- **Model:** `BranchHours { BranchId, DayOfWeek, Opens, Closes }`, several per day allowed (lunch and dinner), a day without any is closed, so no `IsClosed` column is needed. `Closes <= Opens` runs past midnight (18:00–02:00); `Closes == Opens` is open 24 hours. `Branch.HoursEnabled` (off by default, so a branch without hours never looks closed) and `Branch.TimeZone` (default `Europe/Tirane`, a list of nearby zones in the form). Migration `AddOpeningHours`; `BranchHours` has the same soft-delete filter as `Branch`.
- **Logic** (`Helpers/OpeningHours`): status for any moment from the concrete periods of yesterday to 8 days ahead, merged where they touch (18:00–24:00 then 00:00–02:00 reads "open until 02:00"), in the branch's zone. Labels in the 7 menu languages with localised weekday names: "Open until 23:00", "Open 24 hours", "Closed · opens 19:00 / tomorrow 08:00 / Friday 09:00", "Closed". Unit-tested for overnight, split days, 24 hours, wrapping round the week and daylight-saving changes.
- **Branch form:** "Opening hours" section: on/off switch, time zone, seven rows (Closed, times, optional second period), "Copy Monday to all days". Validation per day: both times, second period after the first, past-midnight days have one period, a late night may not run into the next day's opening. Errors are listed in the section and the typed values are kept. With the switch off, valid days are saved for later and nothing blocks saving.
- **Menu:** a chip next to address and phone with a green or red dot **and** the status in words. Tapping it opens the week, with today highlighted. The status is worked out when the page loads.
- **SEO:** `openingHoursSpecification` per period in the Restaurant JSON-LD (24 hours closes at 23:59).
- **Insights** now counts days in the branch's own time zone.

## 8. Time-based menus: done

Breakfast, lunch and happy hour without editing the menu twice a day.

- **Model:** `Category.AvailableDays` (`[Flags]`, `None` = every day), `AvailableFrom` / `AvailableTo` (`TimeOnly?`, both empty = all day, To earlier than From runs past midnight) and `HideWhenUnavailable`. Migration `AddCategorySchedule`; existing categories are served all the time, as before.
- **Logic** (`Helpers/ServingTimes`): the window is turned into opening-hours periods, so `OpeningHours.GetStatus` (overnight, week wrap, the branch's time zone) decides whether a category is served. The wording is in all 7 languages: "Served Mon–Fri · 08:00–11:30", "Available from 17:00 / tomorrow from 08:00 / Monday from 08:00", "Available Saturday" for all-day windows. Unit-tested together with item 7.
- **Category form** (create and edit): "When it's served" section with an "Only at certain times" switch, day chips, from and to times, and greyed-out or hidden outside the times. Each problem gets its own message and the typed values are kept. Branch Details shows each category's schedule.
- **Menu:** a note under the title while the category is served. Outside its times it is greyed out with "Available …", its + buttons are hidden and its nav chip is muted, or it is removed from the page if set to hidden. If everything is hidden the menu says "Nothing is being served right now". The structured data always keeps the full menu, so search engines see the same menu at any hour.
- **Fixed alongside:** the Position field on Category Edit was ignored when saving.

## 9. Specials and featured dishes (1 day)

`Product.IsFeatured`, `Product.Badge` ("New", "Chef's pick"). A horizontal scroll-snap row at the top of the menu with large photos. Owners toggle from the dish row on Branch Details.

## 10. Variants and add-ons (3 to 4 days)

Sizes, half and full, extra cheese. `ProductOption { ProductId, Name, PriceDelta, GroupName, MaxSelect }`. A repeater section in `_ProductForm.cshtml`; the dish sheet in the menu shows option groups; "Your list" stores the chosen options. This is a prerequisite for ordering (item 15).

## 11. Branding editor (2 days)

Colours are currently auto-extracted from the logo (`ColorExtractionService`) with no override. Add a "Brand" section on Branch Edit: three colour pickers prefilled from `ThemeColors`, a light/dark/auto preference, a header style (photo, colour, minimal), and a live phone preview reusing the landing's `.device` component. Save into the existing `ThemeColors` JSON.

## 12. Staff accounts per branch (3 days)

`BranchMember { BranchId, UserId, Role (Manager, Editor) }`. Editors can change dishes and availability but not delete branches. Replace `b.UserId == user.Id` checks with a single `IBranchAccess.CanEdit(user, branchId)` service so the rule lives in one place.

## 13. Invites, email, password reset (2 to 3 days)

- Email provider (Resend, Postmark or SendGrid) behind `IEmailSender`.
- Admin "New owner" sends an invite link (`GenerateEmailConfirmationTokenAsync` + set-password page) instead of showing a password.
- "Forgot password" on the login page (`GeneratePasswordResetTokenAsync`).
- Later: self-serve signup with a trial, which makes the landing's "Get started free" literally true.

---

## Bigger bets (the products the landing page sells)

### 14. Table ordering (3 to 4 weeks)
`Order`, `OrderItem` (with options), `Table`. "Your list" gets a "Send to kitchen" button when the menu was opened with `?t=`. A kitchen display page `/kitchen/{branch}` updates live via **SignalR**, with statuses New → Preparing → Served. Optional online payment later (Stripe, or a local acquirer for Albania).

### 15. Bookings (2 to 3 weeks)
`ReservationSettings` (slot length, covers per slot, lead time), `Reservation { Name, Phone, Guests, StartsAt, Status }`. A public widget at `/book/{slug}` (the landing's slot-picker tile is already the UI), owner calendar view, SMS confirmations through Twilio or a local SMS gateway, and no-show tracking.

### 16. Restaurant websites (2 to 3 weeks)
A `/site/{slug}` template rendering branch data (hero, about, hours, menu highlights, booking button, map). Custom domains: a `Domain` table plus host-based routing middleware that maps `Host` to the branch, and Render custom domains with automatic TLS.

### 17. Management system (ongoing)
Stock per ingredient, staff shifts, end-of-day sales. Build only after ordering exists, since it feeds on order data.

### 18. Guest feedback (1 week)
After the meal (or from the menu footer) a 1-to-5 rating with an optional comment. High ratings get a nudge to leave a Google review; low ratings go privately to the owner.

### 19. Offline-ready menu (PWA, 2 days)
A service worker caching `/menu/{slug}`, its CSS/JS and dish images, so the menu still opens on weak restaurant Wi-Fi.

---

## Suggested order

1. **This week:** section 0 (secrets, migrations, admin), then 1 sold out, 2 QR, 4 dish reorder.
2. **This month:** 3 allergens, 5 analytics, 6 translate-assist, 7 opening hours, 13 invites and reset.
3. **This quarter:** 9 specials, 11 branding editor, 12 staff, 10 variants.
4. **Then:** 15 bookings (smaller than ordering, and already designed on the landing), then 14 ordering, then 16 websites.

## Conventions to keep (from the redesign)

- Tokens live in `wwwroot/css/tokens.css`. Back office components go in `site.css`, the guest menu in `menu.css`, the landing in `landing.css`. Don't add inline colours.
- One icon family: Phosphor (`ph ph-*` / `ph-fill ph-*`).
- Shapes: pills for buttons and chips, 10px for inputs, 18px for panels, 26px for sheets.
- Behaviour in `site.js` is opt-in through `data-*` attributes (`data-confirm`, `data-copy`, `data-dropzone`, `data-sortable`, `data-preview-*`). Add new behaviours the same way.
- Motion only where it explains something, always with a `prefers-reduced-motion` fallback.
- The repo sits on an exFAT drive. `._*` files are excluded in the `.csproj` and `.gitignore`; keep it that way.
