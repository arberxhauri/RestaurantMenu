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
| New owner's password shown on screen | No password is created. The owner gets an invite link (3 days, single use) to set their own: emailed when email is configured, otherwise shown once to the admin with a Copy button. "Invite pending" status and "New invite link" action on the owners list. |
| Blank 404 for unknown menu links | Friendly pages for 404 ("This menu isn't available" for menu links) and 403. |
| Permanent deletes | Branches, categories and dishes are soft-deleted (branches never were, despite the `IsDeleted` column). Every delete shows **Undo**. Dish photos stay on disk so Undo is complete. A deleted branch no longer blocks its name. |

Also fixed while doing this: admins were sent to the owner dashboard (access denied) after changing their password; the access-denied page didn't exist; sign-in keys weren't persisted, so every Render deploy signed everyone out (now stored on the persistent disk under `keys/`).

### Deploy checklist

1. **Render, Postgres:** rotate the database password. Take a backup.
2. **Render, web service, Environment:**
   - `ConnectionStrings__DefaultConnection` = the new connection string (Npgsql format, as before)
   - optional `ADMIN_INITIAL_PASSWORD` (only used if the admin account doesn't exist yet)
   - optional email (see item 13): `Email__From` (an address on your verified domain) plus `Email__ResendApiKey` (or `Email__BrevoApiKey`), and `Seo__BaseUrl` (e.g. `https://myquickmenu.al`) so emailed links always point at your real address. SMTP (`Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`) still works on hosts that allow it, but not on Render's free plan.
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

---

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

## 9. Specials and featured dishes: done

- **Model:** `Product.IsFeatured` and `Product.Badge`, a fixed `DishBadge` enum (New, Chef's pick, Popular, Seasonal, Today's special) instead of free text, so guests see it translated in all 7 languages (`Helpers/Highlights`). Migration `AddFeaturedDishes`.
- **Server:** `ProductController.ToggleFeatured(id, isFeatured?)`, the same contract as the sold-out toggle: antiforgery, ownership check, the desired state is sent, JSON for fetch, redirect for a plain form post. Featured and badge are also saved from the dish form; an unknown badge value is treated as no badge.
- **Back office:** a star on every dish row (instant, goes back if saving fails, works by keyboard and without JavaScript) and a badge chip next to the name. A "Highlight" section in the dish form: "Recommend at the top of the menu" and a badge picker.
- **Menu:** a "Recommended" row at the top: horizontal scroll-snap cards with large photos (a brand-coloured card with the initial if there is no photo) and the badge on the photo. It shows only dishes a guest can get right now (category served, not sold out), in menu order, up to 12. Tapping a card opens the dish's sheet, so adding to the list and analytics work as for any dish. The row hides while searching or filtering. Badges also appear on the dish cards and in the dish sheet.

## 10. Variants and add-ons: done

Sizes, half and full portions, extras. A prerequisite for ordering (item 14).

- **Model (normalised):** `ProductOptionGroup { ProductId, Name, NameTranslations, MinSelect, MaxSelect, DisplayOrder }` with `ProductOption { GroupId, Name, NameTranslations, PriceDelta, DisplayOrder }`, instead of one flat table that repeats the group's rules on every option. MinSelect 0/1 = optional/required; MaxSelect 1 = a single choice. Cascade delete; query filters match the dish's soft delete. Migration `AddProductOptions`.
- **Dish form:** a "Sizes & add-ons" repeater (`_ProductOptions.cshtml`): group name, Required, "Guests can choose up to N", option rows with a price change (negative for cheaper), quick-add buttons for Sizes and Extras, and a "Show translations" switch with a field per menu language. "Fill translations" fills option names too (`opt_…` fields, ≤ 60 characters). Field names are renumbered on submit, so rows can be added and removed freely. Validation (`Helpers/ProductOptions`): names required and unique, valid amounts (2 decimals, comma or dot), max ≥ 1 (clamped to the number of options), at most 10 groups × 30 options; empty rows are ignored; each problem has its own message and the typed values are kept. Saving replaces the dish's groups as a whole.
- **Menu:** the dish's options travel as JSON on the card. Dishes with a required choice that changes the price show "from €5.50". Their + opens the dish sheet: required single choice = round buttons with the first one selected, optional single choice = a box that can be cleared, several = tick boxes stopped at the limit; prices per option, the total updates live, and "Please choose: Sauce" if a required group is missing. Everything is translated in 7 languages.
- **Your list:** each combination is its own line ("Calzone · Large, Cream, Cheese ×2") with the right unit price and total; the same combination again adds 1. Lists saved before this change still work.
- **Later (ordering):** the server must re-check choices and prices from the option ids, since the list's prices live in the guest's browser.

## 11. Branding editor: done

- **Data:** `Branch.ThemeColors` JSON, read and written by `Helpers/BrandTheme`: `Primary`, `Secondary`, `Accent`, `Appearance` (auto / light / dark), `Header` (photo / colour / minimal), and `LogoPrimary/Secondary/Accent` (the colours last taken from the logo, for "Use logo colours"). Every older format still reads (the logo-only JSON, and the lowercase keys of the old fallback). No migration.
- **Roles of the colours:** main = buttons, prices, the selected category; second = blends with the main colour in the header; highlight = dish badges (Chef's pick, New) and the badge on Recommended cards. Before this the accent colour wasn't used anywhere.
- **Contrast is guaranteed rather than refused:** text on a colour is near-black or white, whichever contrasts more, and pure black if neither reaches 4.5:1 (it then always does). Coloured text (prices, badges) is darkened or lightened just enough to reach 4.5:1 on the menu's light and dark backgrounds. Checked over 4,096 colours in the unit tests. The form explains it when a colour is clearly unreadable as chosen (below 3:1) in a mode the menu uses. On save only malformed colours are refused.
- **Logo upload:** the new logo's colours are always remembered and are used on the menu unless the owner changed the colours in the same save.
- **Branch form:** "Brand" section: three colour pickers with hex fields, "Use logo colours", header style and light/dark cards. A live phone preview in the side column (shared `wwwroot/css/device.css`, extracted from the landing page) sticks while editing and has Light/Dark tabs. It uses the same contrast maths in JS, so it shows exactly what guests get.
- **Menu:** forced light or dark (`theme-light` / `theme-dark`, also sets `color-scheme`). Header text and chips follow the header's background (white on photos, chosen black or white on colour, ink when minimal). Photo without a banner falls back to colour, and the banner is only preloaded for photo headers. The printed QR cards use the main colour.

## 12. Staff accounts per branch: done

Managers and waiters update the menu with their own login.

- **Model:** `BranchMember { BranchId, UserId, Role (Editor, Manager), CreatedUtc }`, unique per branch and person, with a query filter matching the branch's soft delete. The owner stays `Branch.UserId`. New `STAFF` role (seeded) for people who only help run branches. Migration `AddBranchMembers`.
- **One access rule:** `Services/BranchAccess` (`IBranchAccess`) decides every back-office action with a single permission table:
  - Editor: view the branch, add/edit dishes, sold out, recommend, reorder dishes, photos, translate dishes.
  - Manager: also delete/restore dishes, categories, branch details/hours/brand, Insights, QR & print.
  - Owner: also the team and deleting the branch.
  Controllers compose `BranchIds(permission)` into their queries (one SQL query each), or call `CanAsync` where a query ignores filters (restores). Every `b.UserId == user.Id` access check is replaced. What remains is intentional: the owner's branch quota on Create, and the owner-only restore of a deleted branch, which the access service can't see. Refused requests answer 404, so nothing reveals that a branch exists.
- **Security fixes found on the way:**
  1. Product Create/Edit didn't check that the posted category belonged to the branch, so a dish could be written into another restaurant's menu. They now refuse with "Choose one of this branch's categories".
  2. Category reorder didn't exclude deleted branches and answered 200 even when it changed nothing. It's now all-or-nothing in one query, 404 for any id the person may not reorder, and never mixes branches.
- **Team section** on Branch Details (owner only): invite by email (optional name, role), change role (saves on selection), remove (takes effect on the next click, since access is checked against the database on every request), "New invite link" for pending invites. New people get an account with no password and set their own through the invite link (`Services/InviteMailer`, now shared with the admin's owner invites: emailed when email is configured, otherwise a one-time link to copy). Existing accounts, including other owners, are added directly. Refused: the owner's own account, administrators, removed accounts, people already on the team. Removing someone keeps their account (its email stays theirs).
- **Dashboard and pages follow the role:** staff see the branches they're on with a Manager/Editor chip and only the actions their role allows. "New branch" and the quota are for owners. The branch page hides what the role can't do, and the server checks again.
- **Later:** managers inviting editors themselves; an activity log (who changed what).

## 13. Invites, email, password reset: done (free)

- **Email providers over HTTPS:** Render's free plan blocks outgoing SMTP (ports 25, 465, 587) since September 2025, so `Services/EmailService` sends through **Resend** (free: 3,000 emails a month, 100 a day) or **Brevo** (free: 300 a day) over their HTTPS APIs. SMTP stays for other hosts. The provider is picked from the settings: `Email__From` plus one key (`Email__ResendApiKey`, `Email__BrevoApiKey`, or `Smtp__Host`); with several, Resend, then Brevo, then SMTP, or force one with `Email__Provider` (`resend`, `brevo`, `smtp`, `none`). Optional: `Email__FromName` (default "My Quick Menu"), `Email__ReplyTo`. Old `Smtp__From` still works as the sender. One retry on 429/5xx/timeouts, with an idempotency key so Resend never sends twice. Errors are logged with the provider's answer, never the key. The deploy log says on startup whether email is on and, if not, why.
- **Emails** (`Helpers/EmailTemplate`): one branded template with a button, a copyable link, a preview line and a plain-text part (better delivery). Every value is encoded, so a name can't inject markup. The button colour passes contrast (5.6:1).
- **Two kinds of link, separate keys and lifetimes** (`Services/AccountTokens`): invites use the email-confirmation token (3 days), resets the password-reset token (2 hours). Neither can be used as the other. Both are single-use, because setting a password changes the account's security stamp, which also ends every other pending link. Invite links sent before this change (reset tokens from the default provider) are still accepted until they expire. Links are built from `Seo__BaseUrl` when it's set, so a forged Host header can't point an emailed link at another site.
- **Forgot password** on the login page: the same answer and the same speed whether or not the address has an account (the email goes out from a background queue, `Services/EmailQueue`), so nobody can test which emails are registered. Limits: 8 requests per 15 minutes per network (then a friendly "wait a few minutes" page instead of a bare 429), and one email per account every 2 minutes. Removed accounts get nothing. The reset page signs you in, clears any lockout, confirms the email, and other devices are signed out within 30 minutes. Without email configured, the page explains to ask the administrator.
- **Invites:** the admin's "New owner" and the owner's Team invite now email the invite (`GenerateEmailConfirmationTokenAsync` + the set-password page), and fall back to the copyable link when email is off or the provider refuses. People already on the platform added to a team get a notice email with a Sign in button. An invite opened after the password is set says "Your password is already set" with Sign in and Reset password buttons. An expired one offers to email a new link.
- **Admin page:** an Email panel (on/off, provider, sender, or what's missing and how to switch it on) and a **Password reset link** tool for any owner or staff account. It emails the link, or shows it to copy when email is off, so nobody is stuck. It's refused for admin accounts: admins reset their own through "Forgot password", so one admin can't take over another. Also fixed: a duplicated "Allowance" column header, and the empty state still mentioned temporary passwords.
- **Setup (free):** create a Resend account, add your domain and its DNS records (SPF and DKIM; DMARC recommended), create an API key, then on Render set `Email__From=hello@yourdomain`, `Email__ResendApiKey=re_...` and `Seo__BaseUrl=https://yourdomain` and redeploy. Brevo works the same way with `Email__BrevoApiKey`.
- **Later:** self-serve signup with a trial, which makes the landing's "Get started free" literally true. Email change with confirmation from the account page.

---

## Bigger bets (the products the landing page sells)

### 14. Table ordering: done
Guests at a table send their list to the kitchen; the kitchen display updates live.

- **Data** (migration `AddTableOrdering`, additive): `DiningTable { Number, Name (area), Code }`, `Order { PublicId, ClientRequestId, TableNumber, TableName, Number (daily), OrderDay, Status, Note, Language, Total }`, `OrderItem { ProductId, Name, Options, OptionIds, UnitPrice, Quantity }`. Orders copy everything they show, so editing or deleting dishes and tables never changes them. `Branch.OrderingEnabled`, `OrdersPaused`, and a daily order counter. Orders older than `Orders:RetentionDays` (default 400) are deleted by the daily cleanup.
- **Table codes:** each table's QR code carries its number and a secret 6-character code (`?t=12&k=7QX4MP`). Only a valid code can send orders, so typing `?t=12` at home isn't enough. QR codes and print runs include the code for tables that are set up. Older printouts (number only) still open the menu and show "show your list to your server". "New code" (one table or all) stops old printouts and links.
- **Placing an order** (`POST /menu/order`, `Services/OrderService`, rules in `Helpers/OrderRules`, unit-tested): prices and option prices always come from the menu, never from the phone. The order is refused (with a message in the guest's language) when ordering is off or paused, outside opening hours, when a dish is sold out or its category isn't served now (naming the dishes), or when choices don't match the dish's option rules. Identical lines are merged, the note becomes one line of at most 200 characters, and limits apply: 40 lines, 20 per line, 100 items, 64 KB. The phone sends a request id, so a retry after a lost response never orders twice. Order numbers restart daily and come from one atomic update, so simultaneous orders never share a number (tested with 28 at once). Limits: 8 orders per table per 10 minutes, and 40 per network address (`Orders__RateLimitPerIp`; restaurant guests often share one Wi-Fi address).
- **Guest menu:** at an ordering table, "Your list" shows a note field and **Send to kitchen**, then "Sent to the kitchen · Order #12". The phone remembers its orders for 12 hours and shows their status (Received, Being prepared, Served, Cancelled), checking every 10 seconds while one is open. With an empty list the floating button follows the newest order. Everything is in the menu's 7 languages.
- **Kitchen display** `/kitchen/{branch}` (Editor and up, new `BranchPermission.Kitchen`): three columns (New, Preparing, Done for the last 3 hours) with large type for a tablet or wall screen, tabs on phones. Cards show the table and area, order number and time, minutes waiting ("Late" after 5 minutes new or 20 preparing), dishes with choices, the note and the total. Buttons: Start, Served, Back to new, Undo, Restore, Cancel. Other features:
  - **Pause orders:** guests are asked to order with their server.
  - **Sound:** a chime on new orders.
  - **Full screen**, and the screen is kept awake.
  - **Light and dark:** follows the device.

  Live through **SignalR** (`/hubs/kitchen`, `Hubs/KitchenHub`). Screens join their branch after the same access check as the page, and every change goes through the server. Two screens tapping at once can't undo each other: the second gets "Another screen already moved…". The list is reloaded on start, after every reconnect and every 30 seconds, so a missed push never leaves a screen wrong. Losing the network shows at once ("Offline · waiting for the network").
- **Tables & ordering page** (`/branch/{id}/tables`, Manager and up): switch ordering on or off, add tables by range with an optional area, rename, remove, new code per table or for all, download a table's QR code, open the table's menu to test, orders per table over 30 days, and print the QR codes. The branch page links to it and, while ordering is on, to the kitchen.
- **Also fixed:** an emptied guest list kept showing the old total and Clear button; visually-hidden labels inside scrolling tables widened pages on phones (Tables, and the admin owner list); oversized posts were logged as server errors.
- **Hosting:** Render supports WebSockets on every plan, nothing to set up. For more than one app instance, add a SignalR backplane (Redis).
- **Later:** online payment (Stripe, or a local acquirer for Albania); "call the waiter / bring the bill" buttons; a printer ticket per order; order history and sales on Insights (feeds the management system).

### 15. Bookings: done
Guests book a table online; the restaurant runs the day from one page.

- **Data** (migration `AddBookings`, additive):
  - `ReservationSettings`, one per branch: on/off, time between slots (15/30/60), guests per time, largest party, minimum notice, days ahead, last booking before closing, auto-confirm, reminder hours, country code, owner emails, and closed days.
  - `Reservation { PublicId, Name, Phone, Email, Guests, StartsAtUtc, StartsAtLocal, Status (Pending, Confirmed, Seated, Cancelled, NoShow), Note, StaffNote, Language, Source, CancelledBy, ReminderSentUtc }`.
- **Availability** (`Helpers/BookingRules`, unit-tested):
  - Times come from the branch's **opening hours**: every slot from opening until the last booking time.
  - Evenings past midnight keep their late times on the day they started.
  - Times skipped when the clocks go forward don't exist; times are converted to UTC in the branch's time zone.
  - A time is open when it's far enough ahead, within the booking window, not on a closed day, and has room for the party.
- **Booking safely** (`Services/BookingService`):
  - Seats are counted and taken inside a transaction that locks the branch row, so two guests can't both get the last seats (tested: 10 at once for 3 places, exactly 3 succeeded).
  - The page sends a request id, so a retry never books twice.
  - Phones are normalised to international format ("069 123 4567" becomes +355691234567) and shown as "+355 69 123 4567".
  - One phone may hold 3 upcoming bookings per restaurant.
  - 10 bookings per 10 minutes per network (`Bookings__RateLimitPerIp`).
  - Everything the guest typed is validated, with messages in their language.
- **Guest booking page** `/book/{slug}`: the restaurant's logo and colours, light/dark like its menu, and the menu's 7 languages.
  - Party size stepper (bigger groups are asked to call), date chips for two weeks plus a date field, and time pills in the landing page's style.
  - Name, phone, optional email and a request, then the booking page.
  - Times update without reloading. A time taken meanwhile says so and refreshes. The page also works without JavaScript.
  - Linked from the menu ("Book a table" while bookings are on, but not when the guest is already at a table), and shareable on Instagram, Google or WhatsApp. The settings page has the link and a QR code (PNG).
- **The guest's link** `/book/{slug}/r/{id}`: status, details and address, **Add to calendar** (.ics), and **Cancel booking** (until the time starts, which frees the seats and tells the owner).
- **Messages:**
  - SMS through **Twilio** (`Sms__TwilioAccountSid`, `Sms__TwilioAuthToken`, `Sms__TwilioFrom`; a Messaging Service SID works too). Or through **any HTTP SMS gateway**, such as a local Albanian provider (`Sms__WebhookUrl`, which receives JSON `{to, message}`, plus an optional `Sms__WebhookToken`).
  - Email to guests who give one, using the existing email setup.
  - Sent in the background in the guest's language: confirmation, "we'll confirm soon", confirmed, cancelled by the restaurant.
  - A **reminder** N hours before, with a cancel link, which cuts no-shows. Each reminder is claimed in the database first, so it goes out once. Bookings made inside the window are skipped.
  - The owner gets an email for each new online booking and each cancellation.
  - Without SMS, bookings still work: confirmation on screen and by email.
- **Day view** `/branch/{id}/bookings` (Editor and up, new `BranchPermission.Bookings`), the "calendar per day":
  - Previous/next/today and a date picker; totals (bookings, guests expected, to confirm, no-shows).
  - Every time of the day with a capacity bar ("4 / 6 guests", flagged over capacity). Runs of free times fold into one line.
  - Each booking shows phone and email links, the guest's request, a staff note, "Added by staff", and the guest's history ("1 no-show before", "Visited 3×").
  - Actions: Confirm or Decline (pending), Seated, No-show or Cancel (confirmed), Undo/Restore. Online guests are told when the restaurant confirms or cancels. Two screens can't overwrite each other.
  - **Add a booking** for phone calls and walk-ins (may go over capacity, with a note).
  - **Close day for online booking** for holidays and private events.
  - The page checks every 30 seconds and offers to refresh when bookings change.
- **Settings** `/branch/{id}/bookings/settings` (Manager and up): all of the above, the message status (SMS provider, email), the booking link with Copy, QR code and PNG download. Turning bookings on without opening hours explains what's missing.
- **Later:** table assignment and floor plan, deposits for large groups (with online payment), waitlist when a day is full, booking stats on Insights.

### 16. Restaurant websites: done
A website per branch, built from the dashboard so it's never out of date, at `/site/{slug}`, a free subdomain, or the restaurant's own domain.

- **Data** (migration `AddWebsites`, additive):
  - `BranchSite { Enabled, Tagline (+translations), About (+translations), Instagram, Facebook, ShowHighlights, ShowMap }`: only what the site needs.
  - `Domain { BranchId, Host (unique), Verified, Token, VerifiedUtc, LastCheckUtc, LastError, RenderId }`.
- **The site** (`SiteController`, `Views/Site/Index.cshtml`, `website.css`):
  - Sticky bar, full-width photo hero (or the brand gradient) with open/closed status.
  - See the menu, Book a table (while bookings are on) and Call buttons.
  - About us, featured dishes (else dishes with photos; sold-out and hidden ones left out), the week's hours with today marked, address, directions and a map.
  - The map loads from Google only when tapped, so there's no Google request or cookie until then.
  - Socials, the menu's 7 languages, the restaurant's colours, light/dark.
  - On phones, a floating Book button.
  - Unpublished sites are a preview for the team only (noindex).
- **SEO:** a Restaurant JSON-LD (address, phone, opening hours, menu, `acceptsReservations`, `sameAs`), hreflang, and a canonical on the site's public address (own domain, else subdomain, else `/site`). The main sitemap lists sites by that address. An own domain gets its own robots.txt and sitemap. robots.txt now also hides `/kitchen/`.
- **Host routing** (`Services/SiteHostMiddleware`, before static files and routing):
  - On a connected host, `/` is the site, `/menu` the menu, `/book` the booking page. Styles, images and the menu's endpoints pass through.
  - The back office, sign-in and other restaurants' pages redirect to the app's own address, so sign-in cookies only live there.
  - `www.` and the bare domain redirect to whichever is connected.
  - Unknown, unconnected or unpublished hosts get a short explanation page (404).
  - Lookups come from a 1-minute in-memory snapshot (`Services/SiteHosts`), refreshed on every change.
- **Free subdomains:** with `Sites__WildcardDomain=yourdomain.al`, every published site is also at `{restaurant}.yourdomain.al`. That needs just two domains on Render (`*.yourdomain.al` and `yourdomain.al`), however many restaurants there are.
- **Own domains** (`Services/DomainService`, Website page):
  - Add up to 2 per site. Names are cleaned (scheme, path, port, case; internationalised names stored as punycode); IP addresses, localhost and the app's own hosts are refused; a domain can belong to only one restaurant.
  - Exact DNS instructions: a CNAME to the app's `onrender.com` address for www/subdomains, `A 216.24.57.1` for root domains.
  - **Verified** only when the app reaches itself through the domain (`/.well-known/mqm-domain` must answer that domain's secret token), so DNS really points here. The check never connects to private addresses.
  - "Check now", plus automatic checks every 10 minutes for 7 days.
  - With `Sites__RenderApiKey` + `Sites__RenderServiceId`, domains are added to, verified at and removed from Render automatically, and Render issues the https certificate. Without them, the admin page lists the domains to add in Render by hand.
- **Website page** `/branch/{id}/website` (Manager and up): Published switch, tagline and about text with translations, Instagram and Facebook (validated), section toggles, what the site takes from the dashboard (cover photo, hours), the address with Copy, and domains with status, DNS records, reasons and Check/Remove.
- **Costs:** the site and subdomains are free. Render offers own domains on paid workspace plans only (Hobby includes 2, Pro 15, more are $0.25/month each), so restaurant domains are a paid add-on in practice.
- **Later:** more templates, a photo gallery, events, and per-site analytics.

### 17. Management system: first version done
Stock, staff shifts and end-of-day sales per branch, plus an overview across branches.

- **Data** (migration `AddManagement`, additive):
  - `Ingredient { Name, Unit, Quantity, LowLevel, CostPerUnit, IsArchived }`.
  - `StockMovement { Kind (Delivery, Usage, Waste, Count, Sale, SaleReturn), Change, QuantityAfter, Note, OrderId, UserName }`.
  - `DishIngredient { ProductId, IngredientId, Quantity per portion }` (the recipes).
  - `Shift { UserId or PersonName, Station, StartsLocal, EndsLocal, Note }`.
  - `DailySales { Date, Orders, Items, Revenue, CancelledOrders, TopDishes, CashTotal, CardTotal, CloseNote, ClosedByName }`.
- **Stock** (`/branch/{id}/stock`, `Services/StockService`):
  - Levels change only through movements, so each change has a reason, an author and the level after.
  - Each change locks its ingredient rows (tested: 10 deliveries at once, none lost).
  - Editors record deliveries, usage, waste and counts. Managers add, edit and archive ingredients and write **recipes** (`/stock/recipes`, per portion).
  - **Table orders use stock through the recipes:**
    - placing an order takes the ingredients out;
    - cancelling puts them back, and restoring takes them out again;
    - an order is synced to its state, so it never counts twice;
    - stock never stops an order.
  - Selling past zero shows "Count needed".
  - Shows low stock (banner, chip), stock value, per-ingredient history, and which dishes use an ingredient.
  - Amounts accept "1.5" or "1,5". Movement kinds and units are parsed strictly; an unknown value no longer silently became a delivery.
- **Shifts** (`/branch/{id}/shifts`): a week rota, team members or anyone by name.
  - Hours per person and per day; shifts can end after midnight; at most 16 h.
  - One person can't be on two shifts at once.
  - Copy last week (clashes skipped).
  - Editors see the rota; managers plan it.
- **Sales** (`/branch/{id}/sales`, `Services/SalesService`), Manager and up:
  - Table orders per business day (the branch's time zone), cancelled ones counted separately.
  - Last 7/30/90 days against the period before: revenue, orders, average order, dishes sold.
  - Revenue per day as a chart with hover detail, and top dishes.
  - **End-of-day close:** cash and card counted, a note and who closed, compared with the table orders.
  - CSV export for the accountant.
  - **Nightly roll-up** (`SalesRollupService`, every 30 minutes, `Sales__RollupIntervalSeconds`): stores each finished day with orders, backfills 35 days, refreshes a day for 6 hours after it ends, and keeps the closing count.
- **Overview** (`/manage`, sidebar "Overview"): every branch the person manages, with today, yesterday, 7 and 30 days, low stock, who's on shift now, and days not closed.
  - Totals per currency; branches with different currencies are never added together.
  - Last 30 days by branch as bars.
- **Also fixed:** back-office messages and dates no longer follow the server's culture (en-GB everywhere). Phone layouts no longer stretch to the widest table (`minmax(0, 1fr)`, also applied to bookings and websites).
- **Later:**
  - Purchase orders to suppliers and stock-value history.
  - Clock in/out and wages.
  - Sales from the till (POS) as well as table orders.
  - Automatic "sold out" when an ingredient runs out.
  - Insights on margins (recipe cost vs price).

### 18. Guest feedback: done
Guests rate their visit from the menu. Low ratings reach the owner privately; high ones are offered the Google review link.

- **Data** (migration `AddFeedback`, additive):
  - `Feedback { BranchId, Rating 1-5, Comment, Contact, TableNumber, Language, Status (New, Read, Resolved), CreatedUtc }`.
  - On `Branch`: `FeedbackEnabled` (on by default), `GoogleReviewUrl`, `FeedbackGoogleForAll`, `FeedbackEmailOwner` (on by default).
- **Guest menu:**
  - A "How was your visit?" card with 5 stars above the footer. Tapping a star opens a sheet; the stars can still be changed there.
  - **1-3 stars:** "What went wrong?" with "Only the restaurant reads this", plus an optional way to reach the guest.
  - **4-5 stars:** an optional comment, then "Would you share it on Google?" with a button to the restaurant's Google review page (new tab, no referrer).
  - One rating per visit: the card shows "Thanks for your feedback today!" for 12 hours.
  - The table number is recorded when the menu was opened from a table QR code.
  - All in the menu's 7 languages; hidden when printing.
- **POST /menu/feedback** (`Services/FeedbackService`):
  - Rating 1-5 required. Comments are cleaned and capped at 1,000 characters. A contact is kept only with low ratings.
  - Limited to 10 per 10 minutes per network (`Feedback__RateLimitPerIp`).
  - Bots (a hidden field filled in, or sent within 1.5 s) get the same thank-you, but nothing is stored.
  - The owner gets an email about every 1-3 star rating, with the comment and contact.
- **Google review link:** the owner pastes it from Google Business Profile → Ask for reviews, or a Place ID (ChIJ…). Only https Google addresses are accepted, so the menu can't send guests elsewhere.
- **Google's policy:** it forbids "review gating", meaning asking only happy guests for reviews. The default follows this item's spec (Google link after 4-5 stars only). The setting **"Offer the Google link after every rating"** stays within the policy, and low ratings still reach the owner privately first.
- **Back office** (Manager and up):
  - Branch Details has a **Guest feedback** panel: 30-day average, number of new ratings, the latest five.
  - `/branch/{id}/feedback`: averages (30 days, all time), stars per level, filters (all, new, 1-3, 4-5), paging, mark read/resolved/reopen, mark all read, delete spam.
  - Settings (Manager and up): prompt on/off, Google link, after every rating, emails.
- **Privacy:** guests' contact details are removed after a year (`Feedback__ContactRetentionDays`); the rating and comment stay.
- **Later:** replying to the guest from the back office, feedback per dish, and ratings on the Overview and Insights.

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
