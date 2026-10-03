# My Quick Menu: what to build next, and how

Written after the October 2026 redesign. Each item says **why** it matters, **what** changes in the data model, and **where** it goes in this codebase (ASP.NET Core 8 MVC, EF Core + Postgres, Razor views, `wwwroot/js`).

Ordered by value per effort. Items 1 to 6 are small and make promises the landing page already makes true.

---

## 0. Fix first (found during the redesign)

| Issue | Risk | Fix |
|---|---|---|
| `appsettings.json` contains the **production** Postgres connection string with password, committed to git | Anyone with repo access owns the database | Rotate the DB password on Render now. Move the string to a Render env var (`ConnectionStrings__DefaultConnection`) and delete it from `appsettings.json`. The git history still has it, so rotation is the real fix. |
| `DbInitializer` seeds `admin@restaurantmenu.com` / `Admin@123` | Known default credentials on prod if the admin was never changed | Read the initial admin password from an env var, and set `MustChangePassword = true` on the seeded admin. |
| Migrations were generated for SQL Server (`nvarchar`), but the app runs on Postgres | `dotnet ef database update` fails on a fresh Postgres DB; schema drift is invisible | Squash: delete `Migrations/`, run `dotnet ef migrations add InitialPostgres` against Postgres, then baseline prod (`__EFMigrationsHistory` insert) so it doesn't try to re-create tables. |
| Admin can soft-delete their own account; the admin appears in the owners table | Lock-out | In `AdminController.Index` filter to `OWNER` role; in `SoftDeleteUser` refuse the current user's id. |
| New owner's password is shown in a flash message | Shoulder-surfing, gets lost | See item 13 (invite email). Until then the flash no longer auto-hides (changed in this redesign). |
| Unknown menu URL returns a blank 404 | Guests scanning an old QR see a browser error page | `app.UseStatusCodePagesWithReExecute("/home/status/{0}")` + a `Status` action/view in the public shell ("This menu has moved"). |
| Category and product deletes are hard deletes | No undo | Make `Category`/`Product` implement `ISoftDeletable` and add query filters, same as `Branch`. |

Already fixed in this pass: client-side validation never loaded (wrong script path), editing a dish ignored "Order", the Edit dish category list only showed one category, the guest favourites crashed on load, dashboard prices were hardcoded to `$`.

---

## 1. Sold out / available toggle (1 day)

**Why:** the landing page sells "Sold out at 19:40? Hide it." This is the most-used mid-service action.

- **Model:** `Product.IsAvailable bool = true`. Migration `AddProductAvailability`.
- **Endpoint:** `ProductController.ToggleAvailability(int id)` `[HttpPost, ValidateAntiForgeryToken]`, returns JSON `{ isAvailable }`. Same ownership check as `Edit`.
- **Back office:** in `Views/Branch/Details.cshtml` add a switch in each `.dish` row. Wire it in `site.js` exactly like the category reorder (fetch + `RequestVerificationToken` header + `mqmToast`).
- **Menu:** in `Views/Menu/Index.cshtml` add `is-soldout` to `.m-item` and a "Sold out" chip (style already exists as `.dui-soldout` on the landing; port it to `menu.css`). Hide the `+` button. Optional branch setting: "hide sold-out dishes entirely".

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
