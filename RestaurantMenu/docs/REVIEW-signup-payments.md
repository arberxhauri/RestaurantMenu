# My Quick Menu: review before adding signup and payments

**Date:** 5 October 2026. **Repo state:** branch `main`, after commit `dc80331` ("Landing page") plus uncommitted work recorded as item 20 in `docs/ROADMAP.md` (privacy and terms pages, the tests project, booking retention).

**Purpose:** input for planning **self-serve signup** and **payments**. This document describes the current system, the constraints, and the problems that affect that work. It deliberately does not contain the plan.

**Scope of this review:**
- A code-level review of the account, branch, admin and public flows.
- A content review of the landing, privacy and terms pages.
- A survey of every feature, for plan gating.
- Not a visual or browser audit of every back-office screen.

Facts marked *(verified)* were checked in the code or by running the app. Facts marked *(verify)* are outside knowledge that must be confirmed before anyone relies on it.

---

## 1. Questions the owner must answer first

These change the plan, and the code can't answer them.

1. **Which payment?**
   - **(a)** Restaurants pay My Quick Menu a subscription (SaaS billing).
   - **(b)** Guests pay restaurants for table orders, or booking deposits.

   These are different projects. (b) needs each restaurant to have its own merchant account (a marketplace or Connect-style setup) and touches ordering, the kitchen and refunds. The roadmap lists (b) as "Later". **Assume (a) unless the owner says otherwise.**
2. **Who can sign up?**
   - Fully open, or approval by an admin first?
   - Albania only, or anywhere?
   - Is a NIPT (Albanian tax number) required at signup? It is required today (`CreateUserViewModel`).
3. **Plans and limits.**
   - Is there a free plan or a trial, and for how long?
   - What does each plan include: branches, features (section 5), staff seats?
   - Which currency (EUR or ALL)? Prices with or without VAT?
4. **How restaurants pay.** By card online and recurring, card once per period, or invoice and bank transfer. This depends on which providers work for the operator's country (section 7).
5. **What happens when payment fails or a plan ends.** Grace period? A read-only back office? Do the **public menus stay online**? Taking a restaurant's menu offline mid-service is severe; existing printed QR codes point to it.
6. **The operator's legal entity.** The legal pages say "4CS" (https://4cs.al/). The exact registered company is needed for invoices, the terms, and the payment provider's onboarding.

---

## 2. What the product is today

ASP.NET Core 8 MVC with Razor views, EF Core 8 and Npgsql (Postgres), and ASP.NET Identity. One web project, `RestaurantMenu/`, plus `RestaurantMenu.Tests/` (xUnit, 125 tests).

It is hosted on **Render**:
- in Docker (`Dockerfile`, which restores and publishes only `RestaurantMenu.csproj`);
- with a persistent disk at `DISK_MOUNT_PATH` (default `/var/data`) holding `images/` and the data-protection `keys/`;
- on **one instance**.

`docs/ROADMAP.md` is the authoritative feature log (items 0 to 20). In summary:

| Area | Where | Notes |
|---|---|---|
| Landing | `/`, `Views/Home/Index.cshtml`, `landing.css/js` | Sells 4 products. Signed-out CTA is "Get in touch" → `Landing:ContactUrl` (default https://4cs.al/) because there is no signup. |
| Legal | `/home/privacy`, `/home/terms` | Written 5 Oct 2026. Terms §2 "Fees": "agreed with us in writing before your account is set up". Privacy "Services we use" lists no payment provider. Both must change with this work. |
| Guest menu | `/menu/{slug}` (`MenuController`) | 7 languages (en, sq, it, de, fr, es, tr), allergens, sold out, schedules, specials, options, branding, offline service worker (`wwwroot/sw.js`), anonymous analytics, feedback. |
| Table ordering and kitchen | `POST /menu/order`, `/kitchen/{branch}`, SignalR `/hubs/kitchen` | `Order.Total` exists; **no payment fields** (`Models/Ordering.cs`, `OrderStatus` New/Preparing/Served/Cancelled). |
| Bookings | `/book/{slug}`, `/branch/{id}/bookings` | SMS via Twilio or a webhook gateway (paid per message by whoever holds the account), email via Resend or Brevo. |
| Websites | `/site/{slug}`, wildcard subdomains, own domains | Own domains cost money on Render (paid workspace; $0.25/month per domain beyond what the plan includes, see item 16) *(verify current Render pricing)*. |
| Management | stock, recipes, shifts, sales, `/manage` overview | Manager role and up. |
| Translate-assist | `TranslateController.Fill` → Google Gemini free tier | One API key shared by all restaurants; rate-limited 20 per minute per user. |
| Back office | Dashboard, Branch, Category, Product, Team, Tables, Bookings, Website, Stock, Shifts, Sales, Feedback, Insights | **English only.** The intended customers are mostly Albanian restaurants. |
| Admin | `/admin` (`AdminController`, role ADMIN) | Owners list, invite or new link, password reset link, remove owner, **branch allowance**, Email panel, domains list. |

---

## 3. How accounts work today (what signup replaces)

All of this was verified in the code.

- **Roles** (`Services/DbInitializer.cs`):
  - `ADMIN`: seeded as `admin@restaurantmenu.com`, password from `ADMIN_INITIAL_PASSWORD` or printed to the log once.
  - `OWNER`: owns branches.
  - `STAFF`: invited helpers with a per-branch role.
- **Creating an owner:** only the admin can, via `AdminController.CreateUser` (`Controllers/AdminController.cs:60`).
  1. The admin enters FullName, Email, NIPT and NumberOfBranches (1 to 100).
  2. The user is created **without a password**, `EmailConfirmed = false`, and given the role OWNER.
  3. An invite link (email confirmation token, 3 days, `Services/AccountTokens.cs`) lands on `Account/SetPassword`, which confirms the email and sets the password.
  4. Delivery is by email through `InviteMailer`, or shown once to the admin to copy if email isn't configured.
- **Staff:** `TeamController.Invite` (owner only) creates STAFF accounts the same way, or adds existing accounts directly.
- **Entitlement = one integer:** `ApplicationUser.NumberOfBranches` (`Models/ApplicationUser.cs`).
  - It is enforced in `BranchController.Create` GET and POST (lines 61 and 81) and on branch restore (line 585).
  - `DashboardController` (line 57) uses it to show "New branch".
  - The admin edits it in `UpdateBranchLimit`.
  - **There is no plan, subscription, trial, feature gating or billing data anywhere.**
- **Removing an owner** (`AdminController.SoftDeleteUser`): `IsDeleted = true`, security stamp rotated, and their branches soft-deleted ("menus offline").
  - Users have a global query filter `!IsDeleted` (`Models/ApplicationDbContext.cs:60`), so removed users can't sign in. Their email stays reserved.
- **Login** (`AccountController.Login`, line 45): `PasswordSignInAsync(..., lockoutOnFailure: false)`. Then `MustChangePassword` → ChangePassword, else Admin or Dashboard (`HomeFor`). `RequirePasswordChangeFilter` blocks the back office until a forced password change is done.
- **Email:** optional. `EmailService` with Resend, then Brevo, then SMTP, configured by `Email__*`. Without it, invites fall back to a copyable link. **Self-serve signup needs email to be configured**, both for verification and for password reset.
- **`ApplicationUser` fields:** FullName, NIPT (required string), NumberOfBranches, IsDeleted, DeletedOnUtc, MustChangePassword, plus Identity's fields. There are no billing, company or address fields.
- **There is no account settings page.** Users can't change their name, email or NIPT, delete their account, or export their data. Only "Change password" exists.

---

## 4. Problems found that affect signup and payments

Ordered by severity. Each one was checked in the code.

### P0: fix before opening signup

1. **Menu links can collide, and a QR code can open another restaurant's menu.**
   - The link is `SeoService.Slug(name)`: spaces removed, lowercased (`Services/SeoService.cs:107`).
   - The uniqueness check compares **whole names**, case-insensitively: `BranchController.cs:87-88`, Edit at 236-237, restore at 591.
   - So "Oliva Kitchen" and "OlivaKitchen" can both exist with the same link.
   - The menu lookup is `FirstOrDefaultAsync(b => b.Name.Replace(" ", "").ToLower() == slug)` with **no ordering** (`MenuController.cs:44`), so either restaurant can come back.
   - The same slug drives `/book/{slug}`, `/site/{slug}`, wildcard subdomains (`Services/SiteHosts.cs:99-108`), the manifest and printed QR codes.
   - Names with `/ ? # %` or other non-URL characters aren't handled.
   - The lookup also runs string functions over every branch on every menu request, with no index.
   - **Suggested direction:** a stored, unique, validated `Branch.Slug` column. Backfill it from the current slug while keeping existing links working, de-duplicate any collisions found in production, and look branches up by slug with an index.
   - With open signup the risk changes from "unlikely" to "someone will do this", by accident or on purpose (squatting a rival's name).
2. **Branch names are globally unique across the platform.** A new signup can't use a name another restaurant took (for example a second "Oliva" in another city). Decide whether the name stays unique, or only the slug, with a suggested alternative ("oliva-durres").
3. **A removed account's email crashes account creation.**
   - Identity's `UserName` has a unique index (`UserNameIndex`), and the app sets UserName = Email.
   - `UserManager.CreateAsync` checks uniqueness through the filtered user set, so it **doesn't see removed users**. The insert then fails with a database exception, which the user sees as a 500 error.
   - This already happens in `AdminController.CreateUser`. `TeamController.Invite` handles it correctly (`IgnoreQueryFilters`, lines 67-79), so copy that approach.
   - Self-serve signup must decide what a returning former customer sees: reactivate, or "contact us".
4. **Login has no brute-force protection.** `lockoutOnFailure: false`, even though the lockout options are configured (`Program.cs:63-64`), and there is no rate-limit policy on login. The login POST also lacks `[ValidateAntiForgeryToken]` (the other account POSTs have it).
   - Open signup plus billing data raises the stakes.
   - Use the existing `AddRateLimiter` pattern (`Program.cs:174-224`) for login and signup.

### P1: build into the signup and payments work

5. **No security headers anywhere:** no CSP, `frame-ancestors`/X-Frame-Options, `X-Content-Type-Options` or Referrer-Policy (searched `Program.cs`, `Services`, `Filters`, `Views`). Billing pages must not be frameable. Every page loads third-party CSS and JS from `unpkg.com` (Phosphor icons) and Google Fonts, and the landing also uses cdnjs (GSAP) and unpkg (Lenis). Keep checkout and billing pages free of unnecessary third-party scripts.
6. **Background work isn't durable.** `Services/BackgroundJobs.cs` is an in-memory bounded channel ("what's queued at shutdown is lost"), and `EmailQueue` is in-memory too. Payment webhooks and subscription state changes need **persisted, idempotent processing**: store the event id, process it once, and allow retries. Don't rely on these queues.
7. **One instance, with state in memory:** rate-limit partitions, `IMemoryCache` (reset cooldowns), the `SiteHosts` snapshot, SignalR (no backplane) and background queues. That's fine for now. Don't design billing so that it requires several instances, and don't keep billing state in memory.
8. **Plan gating has no single place yet.** `Services/BranchAccess.cs` (`IBranchAccess`, `BranchPermission` enum at line 18) is the one place that decides staff permissions per branch, and every back-office controller goes through it. It's the natural point to add "does this branch's plan include X". The switches where features turn on:
   - ordering: `TablesController.cs:69`
   - bookings: `BookingsController.cs:252`
   - website: `WebsiteController.cs:99`
   - own domains: `WebsiteController.AddDomain`, line 136
   - team: `TeamController.Invite`, line 45
   - translate-assist: `TranslateController.Fill`, line 54
   - feedback: `FeedbackController.cs:117`

   Also decide what happens to features already on when a plan is downgraded.
9. **Branch quota check races.** It counts and then inserts with no lock (`BranchController.cs:78-85`). It's harmless with an admin-set allowance; with paid limits, two quick submits can exceed the limit. Do it in a transaction, or re-check after insert.
10. **Legal and landing content must move with the feature:**
    - Terms §2 "Fees" (`Views/Home/Terms.cshtml`) needs real billing terms: renewal, cancellation, refunds, failed payment, price changes, VAT.
    - The Privacy page's "Services we use" needs the payment provider and the billing data stored.
    - The landing CTA in `Views/Home/Index.cshtml` (the `ctaLabel`/`ctaHref` block at the top) goes back to signup.
    - The landing has **no pricing section**.
    - A lawyer review is already pending.
11. **The back office is English only.** The signup and billing pages are the conversion funnel; decide whether they need Albanian. Guest pages already have 7 languages through per-feature tables in `Helpers/*Text.cs`, guarded by `TranslationCoverageTests`.
12. **Account self-service is missing:** an account or billing settings page (name, email change with confirmation, company details for invoices, NIPT, password, delete account). This will be needed for billing details anyway.

### P2: worth knowing

13. About 160 nullable warnings (CS8602/CS8618/CS8604). `ApplicationUser.FullName` and `NIPT` are non-nullable strings without defaults. New code should be warning-free.
14. The seeded admin email is `admin@restaurantmenu.com`, which doesn't match the product's domain. It's cosmetic, but billing emails should come from `Email__From` on the real domain.
15. `Seo__BaseUrl` must be set in production, so emailed links (verification, receipts) and payment-provider return URLs use the real domain and not the host header (`AccountTokens` and `InviteMailer` already prefer it).

---

## 5. Features to consider when designing plans

These are inputs for the owner's decisions in section 1, not recommendations.

| Feature | Costs the operator money? | Natural gate |
|---|---|---|
| Branches | Storage only | Count (`NumberOfBranches` today) |
| Menu, QR printing, allergens, schedules, specials, options, branding, offline, analytics | No | Base |
| Staff accounts (Team) | No | Seats per branch |
| Translate-assist (Gemini) | Free tier, shared quota | Plan or rate |
| Table ordering and kitchen display | No | Plan |
| Bookings | **SMS per message** (Twilio or a gateway) if SMS is configured; email is free up to the provider's limits | Plan; SMS allowance or bring-your-own-gateway |
| Website at `/site` and subdomain | No | Plan |
| Own domain | **Yes** (Render) | Paid add-on |
| Stock, shifts, sales, overview | No | Plan |
| Guest feedback | Email only | Base |

---

## 6. What to reuse, and conventions to keep

- **Email:** `EmailService`, `EmailQueue`, `Helpers/EmailTemplate.cs` (branded, encoded, with a plain-text part) and `InviteMailer`. Token links: `AccountTokens` (an invite-style token for email verification already exists, valid 3 days).
- **Abuse protection:** named policies in `AddRateLimiter` (`Program.cs`); bots on public forms use a honeypot field plus a minimum time (see `FeedbackService`).
- **Migrations:** `dotnet ef migrations add <Name>`; applied automatically at startup (`Services/DatabaseMigrator.cs`). **Additive only** (zero-downtime deploys on Render): new columns need defaults; rename or drop over two deploys.
- **Soft delete:** `ISoftDeletable` and `SoftDeleteInterceptor`, with query filters. Admin removal is soft.
- **Tests:** `RestaurantMenu.Tests` with pure rules in `Helpers/*Rules.cs` (no database), unit-tested. Follow that split: rules in a helper, I/O in a service.
- **UI conventions** (`docs/ROADMAP.md`, "Conventions to keep"):
  - colour tokens in `wwwroot/css/tokens.css`;
  - Phosphor icons only;
  - pill buttons, 10px inputs, 18px panels;
  - behaviour through `data-*` attributes in `site.js`;
  - every flow works without JavaScript;
  - `prefers-reduced-motion` respected.
- **Repo quirk:** it lives on an exFAT drive. macOS writes `._*` files; they are excluded in the `.csproj` and `.gitignore`. Use explicit project paths with `dotnet` (for example `dotnet test RestaurantMenu.Tests/RestaurantMenu.Tests.csproj`).
- **Local development:** Postgres `mqm_dev` (`appsettings.Development.json`); the app serves on `http://localhost:5101`. Projects target .NET 8; the local machine has the .NET 10 SDK (`RollForward=Major` in Debug and in tests).
- **The owner's standing expectation:** every feature is built thoroughly (edge cases, ownership and security checks, migrations, no-JS fallbacks, translations where guests see it, verified end to end in the running app). Each step ends with a plain explanation of what was built, how to use it, and what to do to deploy.

---

## 7. Payment providers: research needed

Unverified starting points. The planner must confirm each one, for the operator's country of incorporation.

- **Stripe** is, as far as I know, **not available to businesses registered in Albania** *(verify)*. If the operator has an EU entity, Stripe Billing becomes the simplest option.
- **Merchant-of-record services** (Paddle, Lemon Squeezy) handle VAT and invoices globally. Check that they pay out to the operator's country and accept this kind of business *(verify)*.
- **PayPal:** check whether Albanian business accounts can **receive** payments and support subscriptions *(verify)*.
- **Local options:** Albanian banks' e-commerce card acquiring (for example Raiffeisen, BKT, Credins) and the local wallet POK. Check for an API, **recurring or tokenised cards**, webhooks and fees *(verify)*.
- **Pragmatic fallback:** manual invoice and bank transfer, with the admin marking a period as paid. B2B customers with a NIPT often pay this way anyway.
- **Albanian tax rules** *(verify with an accountant)*:
  - Albania's fiscalization law requires invoices to be fiscalized through the tax authority's system, with e-invoices for B2B.
  - VAT is 20% above the registration threshold.
  - Either one may decide whether a foreign provider's receipts are enough, or the operator must issue fiscalized invoices itself. That could mean an integration, or an accountant's software, plus a manual step.

---

## 8. Files the planner will likely touch

`Models/ApplicationUser.cs` and `Models/Branch.cs` (new slug, plan and billing fields or entities), `Models/ApplicationDbContext.cs`, `Controllers/AccountController.cs`, `Controllers/AdminController.cs`, `Controllers/BranchController.cs`, `Controllers/DashboardController.cs`, `Controllers/MenuController.cs`, `Services/SeoService.cs`, `Services/SiteHosts.cs`, `Services/BranchAccess.cs`, `Program.cs` (rate limits, headers, webhook endpoint), `Views/Home/Index.cshtml`, `Views/Home/Terms.cshtml`, `Views/Home/Privacy.cshtml`, `Views/Admin/*`, `Views/Account/*`, `Views/Shared/_Layout.cshtml` (account and billing navigation), `docs/ROADMAP.md`.
