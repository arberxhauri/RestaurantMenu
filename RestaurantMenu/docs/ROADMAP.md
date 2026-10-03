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

## 2. QR codes and printable table tents (1 to 2 days)

**Why:** every restaurant needs this on day one, and it's what the guest actually scans.

- **Server:** NuGet `QRCoder`. `BranchController.Qr(int id, int? table, string format = "svg")` returns `image/svg+xml` or PNG for the URL `/menu/{slug}?t={table}`.
- **Print page:** `Views/Branch/Print.cshtml` with `@media print` and A6 tent / A4 sticker-sheet layouts: logo, "Scan for the menu", QR, table number. Owner picks tables 1 to N.
- **Menu:** read `t` in `MenuController.Index`, pass `ViewBag.Table`, show "Table 14" under the name (the landing demo already shows this) and include it in "Your list".
- **Dashboard:** add "QR & print" to the branch card dropdown and the Branch Details header actions.

## 3. Structured allergens and dietary tags (2 days)

**Why:** EU Regulation 1169/2011 requires the 14 allergens to be available; guests filter by vegan, gluten-free and similar.

- **Model:** `[Flags] enum Allergen { Gluten = 1, Crustaceans = 2, Eggs = 4, Fish = 8, Peanuts = 16, Soy = 32, Milk = 64, Nuts = 128, Celery = 256, Mustard = 512, Sesame = 1024, Sulphites = 2048, Lupin = 4096, Molluscs = 8192 }` and `[Flags] enum Diet { Vegan, Vegetarian, Spicy, GlutenFree, Halal }`. Store as `int` on `Product`.
- **Form:** in `_ProductForm.cshtml` reuse the `.lang-chips` checkbox-chip pattern. Bind with `int[] allergens` and OR them in the controller.
- **Menu:** icons per allergen (Phosphor has most; keep labels translated in the `ui` switch at the top of the menu view), plus a filter sheet next to the search button ("Hide dishes containing: milk, gluten…"). Filtering reuses `filter()` in `menu.js`.
- Keep the free-text `Nutritions` field for calories and notes.

## 4. Drag to reorder dishes (half a day)

- **Endpoint:** `ProductController.UpdateOrder([FromBody] UpdatePrioritiesRequest)` that sets `DisplayOrder = index` (copy `CategoryController.UpdatePriorities`).
- **UI:** in `Branch/Details.cshtml` add `data-sortable` to each `.dish-group` and a `.drag-handle` per dish. Generalise the Sortable block in `site.js` to read the endpoint and id attribute from data attributes.

## 5. Menu analytics (2 to 3 days)

**Why:** owners renew when they can see that guests use the menu.

- **Model:** `MenuEvent { Id, BranchId, ProductId?, Type (View, DishOpen, AddToList, LanguageSwitch), Lang, CreatedUtc }`, plus a nightly roll-up `MenuDaily { BranchId, Date, Views, DishOpens… }` once volume grows.
- **Capture:** `MenuController.Index` writes a `View` (skip bots by user-agent). `menu.js` sends `navigator.sendBeacon('/menu/event', …)` on dish open and add-to-list. No cookies or personal data, so the privacy page stays true.
- **Dashboard:** "Last 7 days" stats strip on the dashboard and a Branch → Insights tab: views per day, top opened dishes, languages used. Charting: Chart.js from cdnjs.

## 6. Translate-assist (1 to 2 days)

**Why:** translations are the slowest part of setup, and the multilingual menu is a headline feature.

- `TranslationService` calling DeepL or the Claude API (`claude-haiku-4-5` is enough) with the dish name, description, nutrition and target languages. Return JSON and prefill the `translation_*` inputs; the owner reviews before saving.
- Button "Fill translations" above the language tabs in `_ProductForm.cshtml` and in `_CategoryTranslations.cshtml`. Key in Render env vars.
- Also translate the UI strings: the guest-facing labels currently live in the `ui` switch at the top of `Views/Menu/Index.cshtml`. Have a native speaker review them, then move them to `.resx` resources.

---

## 7. Opening hours and "Open now" (2 days)

- **Model:** `BranchHours { BranchId, DayOfWeek, Opens TimeOnly, Closes TimeOnly, IsClosed }`, plus `Branch.TimeZone` (default `Europe/Tirane`).
- **Form:** a section in `_BranchForm.cshtml`: 7 rows with time inputs and "Closed".
- **Menu:** "Open until 23:00" or "Closed, opens 08:00" chip next to address and phone.
- **SEO:** add `openingHoursSpecification` in `Helpers/StructuredData.ForBranchMenu`, which helps Google Maps results.

## 8. Time-based menus (2 days)

Breakfast, lunch and happy hour. `Category.AvailableFrom/To` (`TimeOnly?`) and `Category.Days` (flags). The menu greys out or hides categories outside their window (server-side with the branch time zone). The form gets the fields on Category Edit.

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
