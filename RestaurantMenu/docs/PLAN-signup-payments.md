# My Quick Menu: signup, subscriptions and payments plan

**Date:** 5 October 2026 · Arbër Xhauri
**Builds on:** `docs/REVIEW-signup-payments.md` (the review this plan answers).
**Status:** agreed plan, not started.

## Summary

Build it in six phases:
1. Fix the P0 problems.
2. Add a plan catalogue and entitlements.
3. Open self-serve signup with a free trial.
4. Add a provider-neutral billing core that starts with bank transfer.
5. Add card payments through Paddle.
6. Finish with account, legal and admin tooling.

Restaurants pay My Quick Menu (SaaS billing, option a). Guest payments stay out of scope.

The subscription is **modular**: every restaurant gets the base menu, then switches on only the modules it needs (ordering and kitchen, bookings, website, management, own domain), priced per branch. The price the restaurant sees is built from those choices at signup and can change later.

---

## Recommended answers to the owner's questions

These are defaults the plan is written against. Change any of them and the affected phase is noted.

| Question | Recommended answer | Why | Affects |
|---|---|---|---|
| Which payment | (a) Restaurants pay a subscription | Guest payments need a merchant account per restaurant and touch ordering and refunds | All |
| Who can sign up | Open, with an admin switch to require approval (`Signup:RequireApproval`) | Lets you start gated and open later without code changes | Phase 2 |
| Where | Albania first; the code stays country-aware (country field, currency per country) | Tax, invoicing and payment options are Albania-specific today | Phases 2, 3 |
| NIPT at signup | Optional at signup, required before the first paid invoice | A NIPT at the first screen loses leads; invoices need it | Phases 2, 5 |
| Free plan or trial | 14-day trial of any modules chosen, no card needed; no permanent free plan at launch | Card adoption for B2B in Albania is low; a card-first trial would block most signups | Phases 2, 3 |
| Currency and VAT | Prices in EUR, shown without VAT, VAT added on the invoice where it applies | Matches the landing's € prices and Paddle's Albania currency (EUR) | Phases 1, 3 |
| How restaurants pay | Bank transfer with an invoice first; card (monthly or yearly, recurring) through Paddle second | Bank transfer works on day one and matches how NIPT holders pay | Phases 3, 4 |
| When payment fails | 14-day grace, then the back office goes read-only and paid modules pause; **public menus never go offline for non-payment** | Printed QR codes point to the menu; taking it down mid-service is severe | Phase 3 |
| Legal entity | **Still needed from the owner:** exact registered name, NIPT, address and bank account of the operator | Required for invoices, terms and Paddle onboarding | Phases 3, 4, 5 |

---

## What the live site and research showed

**myquickmenu.com today (checked 5 Oct 2026).** The landing matches the review: four products (Digital menus, Management system, Restaurant websites, Bookings), the line "Start with the menu and add the rest when you need it", two live restaurants in the partners strip (Oliver's Italian, Tirana Palace), and both CTAs ("Get in touch") pointing to 4cs.al. There is a "Sign in" link but no signup and no pricing. The copy already sells a modular product, so a build-your-plan pricing page fits the message without rewriting the landing.

**One thing to fix with the slug work:** Oliver's Italian's menu link is `/menu/oliver%27sitalian`. The apostrophe survives into the URL, which is the P0 slug problem already visible in production.

### Payment providers for a business registered in Albania

| Provider | Works for an Albanian business? | Recurring cards | Who handles VAT and invoices | Fees and payouts | Fit |
|---|---|---|---|---|---|
| Stripe | No, Albania is not a supported merchant country | Yes | You | n/a | Only if the operator has an EU or other supported entity |
| Paddle | Yes, works with software businesses anywhere except sanctioned countries; sells to Albania in EUR | Yes, with upgrades, downgrades and proration | Paddle, as merchant of record | About 5% + $0.50 per transaction, monthly payouts, $100 minimum, a SWIFT fee | **Recommended card provider** |
| Lemon Squeezy | Bank payouts to Albania listed | Yes | Lemon Squeezy, as merchant of record | Check current terms | Backup to Paddle |
| RaiAccept (Raiffeisen Bank Albania) | Yes, with a Raiffeisen business account; ALL and EUR | Returns a card token on a recurring order; you schedule the charges | You, including fiscalization | Negotiated with the bank | Option if you want ALL pricing and local acquiring |
| POK | Yes, Albanian wallet with an API, SDKs and pay-by-link | Unconfirmed for merchant-initiated recurring | You | Check with POK | Pay-by-link for invoices, later |
| Bank transfer | Yes | No | You | Bank fees only | **Day-one method**; most NIPT holders pay this way |

**What this means for the design.** No single provider covers everything, so billing sits behind an `IBillingProvider` interface. Bank transfer ships first. Paddle is the first card provider because, as merchant of record, it carries VAT and card receipts; your own accounting then mostly deals with Paddle's payouts. RaiAccept can be added later behind the same interface. With RaiAccept, your app runs the renewal schedule and issues fiscalized invoices itself.

**Tax still needs an accountant.** Two points to confirm:
- whether Albanian restaurants accept Paddle's receipts for their own books or ask for a local fiscalized invoice;
- how the operator fiscalizes the invoices it issues for bank-transfer customers (Albania's fiscalization system, 20% VAT above the registration threshold).

The plan stores everything a fiscal invoice needs (sequential number, NIPT, VAT, a field for the fiscal code) so either answer fits.

---

## Pricing model

A subscription is a list of modules times a branch count, billed monthly or yearly. The restaurant ticks what it needs, sets how many branches, and sees the total update. Prices are the owner's call; the table fixes the structure and leaves the amounts open.

| Module (key) | What it switches on | Billed as | Required | Gate in code |
|---|---|---|---|---|
| `Menu` (base) | Guest menu, QR printing, allergens, schedules, specials, options, branding, offline, analytics, guest feedback, translate-assist within a fair-use limit | Per branch per month | Yes | Branch count |
| `Ordering` | Table ordering and kitchen display | Per branch per month | No | `TablesController.cs:69` |
| `Bookings` | Booking page and back office, email confirmations | Per branch per month | No | `BookingsController.cs:252` |
| `Website` | `/site/{slug}` and the free subdomain | Per branch per month | No | `WebsiteController.cs:99` |
| `Management` | Stock, recipes, shifts, sales, `/manage` overview | Per branch per month | No | Stock, Shifts, Sales controllers |
| `OwnDomain` | Custom domains on the website | Per domain per month | Needs Website | `WebsiteController.AddDomain` |
| `Sms` | Booking SMS through the platform's gateway | Per message pack, or free with the restaurant's own gateway | Needs Bookings | SMS sender |
| Staff seats | Team accounts | A number included per branch (for example 5); more as an add-on later | n/a | `TeamController.Invite` |

Rules that keep it simple:
- **Modules apply to the whole account**, and the branch count multiplies every per-branch module. Different modules on different branches can come later; it complicates the price page and the gating for little gain at this size.
- **Three presets** on the pricing page pre-tick modules (for example "Menu", "Menu + Website + Bookings", "Everything"), so most people pick in one click. A preset is only a starting selection, never a separate plan in the data.
- **Yearly billing gets a discount** (for example two months free). Store it as its own price, not a computed rule, so it can change.
- **A price change never touches existing subscriptions** until their next renewal, and only after an email notice (the terms will say how many days).
- **Existing customers move to a Legacy subscription** that keeps every module on and their current `NumberOfBranches`, with no charge until the owner decides to move them. Nobody loses a feature on the day this ships.
- **SMS costs real money per message.** Until there is an SMS pack, bookings send email by default and SMS only for accounts with their own gateway or an admin-granted allowance.

---

## Data model

Billing hangs off the owner (`ApplicationUser` with role OWNER), one subscription per owner. All changes are additive migrations with defaults, per the zero-downtime rule.

### Changed tables

| Table | New columns | Notes |
|---|---|---|
| `Branch` | `Slug` (varchar 80, unique index, not null after backfill) | Backfill in the migration: slugify the current name, add `-2`, `-3` on collisions, log each collision. `Name` stops being globally unique. |
| `ApplicationUser` | `Country` (default `AL`), `SignupSource` (Admin, SelfServe), `TermsVersion`, `TermsAcceptedUtc`, `ApprovedUtc` (nullable) | `NIPT` becomes nullable in a later deploy; keep the column. `NumberOfBranches` stays for legacy reads until phase 6. |

### New tables

| Table | Key columns | Purpose |
|---|---|---|
| `BranchSlugAlias` | `Slug` (unique), `BranchId` | Old links (including `oliver%27sitalian`) keep resolving after a rename or the backfill |
| `PriceBook` | `Module`, `Interval` (Month/Year), `Currency`, `UnitAmountCents`, `ValidFromUtc`, `PaddlePriceId` | Prices live in data so they can change without a deploy |
| `Subscription` | `OwnerId` (unique), `Status`, `Interval`, `Currency`, `BranchQuantity`, `TrialEndsUtc`, `CurrentPeriodStartUtc`, `CurrentPeriodEndUtc`, `GraceEndsUtc`, `CancelAtPeriodEnd`, `Provider` (None, BankTransfer, Paddle), `ProviderCustomerId`, `ProviderSubscriptionId`, `IsLegacy`, `xmin` concurrency token | One row per owner; the single source of truth for entitlements |
| `SubscriptionItem` | `SubscriptionId`, `Module`, `Quantity`, `UnitAmountCents` | The modules switched on, priced at the time they were added |
| `BillingProfile` | `OwnerId`, `LegalName`, `NIPT`, `Address`, `City`, `Country`, `BillingEmail`, `VatRegistered` | What goes on invoices; edited on the billing page |
| `Invoice` + `InvoiceLine` | `Number` (sequential per year, unique), `OwnerId`, period, `SubtotalCents`, `VatCents`, `TotalCents`, `Currency`, `Status` (Draft, Open, Paid, Void), `DueUtc`, `PaidUtc`, `PaidMethod`, `ProviderRef`, `FiscalCode` (nullable) | Bank-transfer invoices issued by you; Paddle transactions mirrored for history |
| `BillingEvent` | `Provider`, `EventId` (unique with provider), `Type`, `PayloadJson`, `ReceivedUtc`, `ProcessedUtc`, `Attempts`, `LastError` | Webhook inbox: stored first, processed once, retried on failure |
| `BillingEmailLog` | `OwnerId`, `Kind`, `PeriodKey`, `SentUtc` (unique on owner, kind, period) | Stops trial and dunning emails from going out twice, since `EmailQueue` is in memory |
| `SubscriptionAudit` | `SubscriptionId`, `ActorId`, `Action`, `FromJson`, `ToJson`, `AtUtc` | Who changed what, including admin overrides |

Module keys are an enum (`BillingModule`) in code; the price book references them. **Store money as integer cents.**

---

## Entitlements and feature gating

One service answers "what may this account do right now", and every gate asks it. **Nothing else reads `Subscription` or `NumberOfBranches` directly.**

- **Pure rules:** `Helpers/EntitlementRules.cs` turns a subscription snapshot plus the current time into an `Entitlements` record: the set of modules, `MaxBranches`, `SeatsPerBranch`, and an `Access` level (Full, ReadOnly, Suspended). No database, fully unit-tested, like the other `*Rules.cs` helpers.
- **Service:** `Services/EntitlementService.cs` (`IEntitlementService.ForOwnerAsync`, `ForBranchAsync`) loads the subscription and calls the rules. Scoped per request, so one load per request and nothing cached across requests.
- **One gate:** extend `IBranchAccess` with `HasModuleAsync(branchId, BillingModule)` and `CanWriteAsync(branchId)`. Back-office controllers already go through it, so the seven switch points in the review each become one call.
- **Read-only mode:** a filter (`RequireWritableAccountFilter`, next to `RequirePasswordChangeFilter`) blocks POSTs in the back office when Access is ReadOnly, except billing, account and sign-out. GET pages still work and show a banner with the reason and a pay button.
- **Branch quota:** `BranchController.Create` and restore take a Postgres advisory lock on the owner (`pg_advisory_xact_lock`) inside a transaction, count, then insert. This fixes the race in review P1-9.

When a module is off (never paid, or paused after a downgrade or lapse), its data stays and its public face degrades gently:

| Module off | Back office | What guests see |
|---|---|---|
| Ordering | Tables and kitchen pages show an upsell card | Menu works; the order button is hidden; `POST /menu/order` returns a clear refusal |
| Bookings | Existing bookings visible, no new settings | Booking page says online booking is not available and shows the phone number |
| Website | Website editor read-only | `/site/{slug}` and the subdomain redirect to `/menu/{slug}` |
| OwnDomain | Domain list kept, marked paused | Own domain redirects to `/menu/{slug}` on the main host; remove it from Render after 30 days so it stops costing |
| Management | Stock, shifts, sales read-only | Nothing changes |
| Branches over the limit | Owner picks which branches stay active; the rest are paused (not deleted) | Paused branch menus still load, with no ordering or bookings |

**The guest menu itself (`/menu/{slug}`) is never gated by billing.**

---

## Subscription lifecycle

Five states, all computed by `SubscriptionRules`: **Trialing, Active, PastDue, ReadOnly, Cancelled**. Payment moves an account back to Active from any of them except Cancelled, which needs a new checkout.

```
Trialing ──paid──────────────► Active ◄──────────── paid ──┐
   │                            │   │                      │
   │ trial ends unpaid          │   │ renewal unpaid       │
   │                            │   ▼                      │
   │                            │ PastDue ── grace ends ──► ReadOnly
   └────────────────────────────┼──────────────────────────► ReadOnly
                                │
             cancel at period end, period ends
                                ▼
                            Cancelled  (needs a new checkout)
```

- A trial that ends unpaid goes straight to ReadOnly, since there is nothing to be overdue on.
- An owner who cancels stays Active until the period ends, then moves to Cancelled.
- Legacy subscriptions sit in Active with no period end until the owner moves them.
- **Upgrades** take effect at once (Paddle prorates; bank transfer adds a pro-rata line to the next invoice).
- **Downgrades and branch reductions** take effect at the next period, and the owner picks which branches stay active.
- In every state the guest menu loads; only the paid modules on top of it pause.

---

## Signup flow

Signup creates an owner on a 14-day trial with the modules they picked, and gets them to a live menu before asking for money. It lives in a new `SignupController` and works without JavaScript; JavaScript only adds the live price and the live slug check.

1. **Pricing** (`/pricing`, also a section on the landing). Presets plus module checkboxes, branch count, monthly or yearly. The total is computed server-side by `PricingRules.Quote` and rendered; JS recalculates as boxes change. "Start free trial" carries the selection to step 2 in the query string.
2. **Account** (`/signup`). Full name, email, password, restaurant name, country, and a terms checkbox (stores `TermsVersion` and the time). The restaurant name shows its menu link (`myquickmenu.com/menu/oliva-durres`) and suggests a free alternative if the slug is taken. Honeypot field plus minimum fill time, and a `signup` rate-limit policy (for example 5 per hour per IP).
3. **Verify email.** The account is created unconfirmed; the email holds a verification link (reuse `AccountTokens`, 3 days). Sign-in is refused until verified, with a "resend link" button rate-limited per address. Email must be configured, so signup is disabled at startup with a log warning if `Email__*` is missing.
4. **First sign-in → onboarding.** A three-step checklist on the dashboard: create the first branch (prefilled with the restaurant name and slug), add a category and a few dishes, print the QR. The trial clock starts at verification, not at form submit.
5. **During the trial.** A dashboard banner shows days left and a "Choose how to pay" button. Emails at 7 days, 3 days and 1 day before the end.
6. **Choose how to pay** (`/billing/checkout`). Confirm the modules and branches, fill the billing profile (legal name, NIPT, address), then pick card (Paddle checkout) or bank transfer (an invoice with the IBAN and a reference). The trial ends normally either way; a bank-transfer invoice has its own due date.

Edge cases to handle:
- **An email that belongs to a removed account** (review P0-3): show "This email was used before. Contact us to reactivate it." and log it. Never surface the database error. Reactivation stays an admin action.
- **Approval mode on:** after verification the owner lands on a "we'll review your account" page; the admin approves from `/admin`, which sets `ApprovedUtc` and starts the trial.
- **Admin-created owners** keep working through `AdminController.CreateUser`, now creating a subscription too (legacy, trial, or a chosen set of modules).
- **Language:** the pricing, signup, verification, onboarding and billing pages, and their emails, are in Albanian and English, using the same `Helpers/*Text.cs` pattern and the coverage test. The rest of the back office stays English for now.
- **One trial per business:** block a second trial for the same NIPT or verified email domain where it is a company domain; the admin can override.

---

## Payments architecture

The app owns the subscription state; providers only report money moving. Every provider sits behind one interface, and every provider message goes through a persisted inbox before it changes anything.

**`IBillingProvider`** (`Services/Billing/`):
- `StartCheckoutAsync(subscription)` returns a redirect or an invoice
- `ChangeItemsAsync`
- `CancelAsync(atPeriodEnd)`
- `GetManageUrlAsync`
- `ParseWebhook(request)` returning a normalized event (PaymentSucceeded, PaymentFailed, SubscriptionCanceled, SubscriptionUpdated) with the provider's event id

Implementations:
- **`BankTransferProvider`** (phase 3). Checkout creates an `Invoice` (status Open, due in 14 days, the operator's IBAN and a payment reference such as `MQM-2026-0042`) and emails it as a PDF. The admin marks it paid in `/admin/billing`; that writes a `BillingEvent` of type PaymentSucceeded through the same inbox, so the state logic is identical for both providers. A renewal invoice is created 14 days before the period ends.
- **`PaddleProvider`** (phase 4). Checkout uses Paddle's hosted checkout with the customer's modules as items and the branch count as quantity, carrying the owner id in `custom_data`. Plan changes go through Paddle's subscription update with proration. "Manage card" links to Paddle's customer portal. Webhooks arrive at `POST /billing/webhooks/paddle`; the signature (`Paddle-Signature`, HMAC with the endpoint secret) is checked before anything is stored.
- **Later, `RaiAcceptProvider`.** The first payment stores the card token; the billing worker charges it on each renewal. Same events, same inbox.

**The inbox and the worker** (replaces the in-memory queues for billing):
1. The webhook endpoint verifies the signature, inserts a `BillingEvent` (unique on provider and event id, so a duplicate insert is a no-op), and returns 200 at once. It is excluded from antiforgery and has its own rate-limit policy.
2. `BillingWorker` (a `BackgroundService`) polls every 30 seconds for unprocessed events, oldest first, with `FOR UPDATE SKIP LOCKED`. Each event is applied in one transaction: update the subscription, write the audit row, mark the event processed. A failure increments `Attempts`, stores the error, and retries with backoff; after 10 failures it alerts the admin.
3. The same worker runs time-based transitions each minute: trial ended, grace ended, period ended with cancel-at-period-end, renewal invoice due. Each transition is a pure function in `SubscriptionRules`, unit-tested with fixed clocks.
4. Billing emails are sent from the worker, guarded by `BillingEmailLog`, so a restart never sends a reminder twice or loses one.

This works on one Render instance and stays correct if a second instance is ever added, because nothing billing-related lives in memory.

**Security for billing pages:** add security headers app-wide (review P1-5): `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'none'` everywhere except where the menu is meant to be embedded. Billing and checkout pages load no unpkg, cdnjs or Google Fonts assets; serve Phosphor and the font locally on those pages, and allow only Paddle's script and frame origins there.

---

## Implementation phases

Six phases, each deployable on its own and each ending with the usual write-up (what was built, how to use it, how to deploy). Signup stays hidden behind `Signup:Enabled=false` until phase 3 is live, so phases 1 and 2 ship without changing what customers see.

### Phase 0: P0 fixes (before anything else)
- [x] Add `Branch.Slug` and `BranchSlugAlias`; backfill with collision suffixes; look up menus, bookings, sites, subdomains and the manifest by indexed slug, falling back to the alias table (`MenuController.cs:44`, `SiteHosts.cs:99-108`, `SeoService.Slug`)
- [x] Slug rules in `Helpers/SlugRules.cs`: lowercase ASCII, Albanian letters transliterated (ë→e, ç→c), spaces and punctuation to `-`, 3 to 60 characters, a reserved list (admin, api, billing, signup, www and the rest of the routes)
- [x] Drop the global unique-name check; keep uniqueness on the slug only; rename keeps the old slug as an alias
- [x] `AdminController.CreateUser`: check removed users with `IgnoreQueryFilters` like `TeamController.Invite`
- [x] Login: `lockoutOnFailure: true`, a login rate-limit policy, `[ValidateAntiForgeryToken]` on the POST

**Accept when:** two branches named "Oliva Kitchen" and "OlivaKitchen" get different links; `/menu/oliver%27sitalian` still opens Oliver's Italian; a removed user's email gives a message, not a 500; the sixth wrong password locks the account.

### Phase 1: catalogue, subscriptions and gating (no payments yet)
- [x] Migrations for `PriceBook`, `Subscription`, `SubscriptionItem`, `BillingProfile`, `SubscriptionAudit`; seed prices from config
- [x] Create a Legacy subscription for every existing owner (all modules, `BranchQuantity = NumberOfBranches`)
- [x] `EntitlementRules`, `EntitlementService`, `IBranchAccess.HasModuleAsync`; wire the seven switch points and the branch quota (with the advisory lock)
- [x] Read-only filter and banners; the "module off" behaviours in the gating table
- [x] Admin: a subscription panel per owner (change modules, branch count, extend trial, mark legacy) replacing `UpdateBranchLimit`

**Accept when:** existing customers see no change; an admin can switch a test owner's Bookings off and the booking page degrades as described.

### Phase 2: self-serve signup and trial
- [x] `PricingRules.Quote` and the `/pricing` page; pricing section and "Start free trial" CTA on the landing (behind the flag)
- [x] `SignupController`: account form, slug suggestion, honeypot, rate limit, verification email, approval mode
- [x] Onboarding checklist on the dashboard; trial banner
- [x] Albanian and English text for the funnel pages and emails, with coverage tests

**Accept when:** with the flag on in staging, a new restaurant can sign up, verify, create a branch and open its live menu in under ten minutes, in either language, without JavaScript.

### Phase 3: billing core and bank transfer
- [ ] `Invoice`, `InvoiceLine`, `BillingEvent`, `BillingEmailLog`; sequential invoice numbers per year
- [ ] `SubscriptionRules` (the lifecycle above) and `BillingWorker`
- [ ] `BankTransferProvider`, invoice PDF (QuestPDF) and email; `/admin/billing` with open invoices, mark paid, void, and resend
- [ ] `/billing` page for owners: current modules, next invoice, invoices list, change modules (takes effect next period for bank transfer)
- [ ] Trial and dunning emails

**Accept when:** a trial ends, the owner chooses bank transfer, the admin marks the invoice paid and the account is Active; an unpaid invoice moves the account to PastDue, then ReadOnly after grace, with the menu still public. **Turn signup on here.**

### Phase 4: card payments with Paddle
- [ ] Paddle account onboarding (needs the legal entity); sandbox first
- [ ] Mirror the price book into Paddle prices (one per module per interval); store `PaddlePriceId`
- [ ] `PaddleProvider`: checkout, webhook endpoint with signature check, update with proration, cancel, customer portal link
- [ ] Security headers and a third-party-free layout for billing pages

**Accept when:** in the Paddle sandbox, a card payment activates the account; a declined renewal moves it to PastDue; replaying the same webhook ten times changes nothing.

### Phase 5: account, legal and landing
- [ ] `/account`: name, email change with confirmation of the new address, password, billing profile, delete account (soft delete with a 30-day restore window, data export as JSON)
- [ ] Terms §2 Fees rewritten: renewal, cancellation, refunds, failed payment, price-change notice, VAT; Privacy "Services we use" adds Paddle and the billing data kept; bump `TermsVersion` and ask existing owners to accept on next sign-in
- [ ] Lawyer review of both pages

### Phase 6: clean-up and reporting
- [ ] Move legacy customers onto priced subscriptions when the owner decides, with a notice period
- [ ] Retire `NumberOfBranches` reads (keep the column one more deploy, then drop)
- [ ] Admin numbers: active subscriptions, monthly recurring revenue, trials converting, overdue invoices
- [ ] SMS packs, extra staff seats, RaiAccept or POK if there is demand

---

## Configuration, deployment and testing

New settings on Render (environment variables, double underscore for sections):

| Setting | Example | Needed from |
|---|---|---|
| Signup on/off, approval, trial days, staff per branch, grace days, currency, prices | **In the database, Admin → Plans & prices** (decided 5 Oct 2026). The `Signup__…`/`Billing__…` env vars only seed the first start | Phases 1–2 |
| `Billing__InvoiceDueDays` | `14` | Phase 3 |
| `Billing__Operator__LegalName`, `__Nipt`, `__Address`, `__Iban`, `__Bank` | the operator's details | Phase 3 |
| `Billing__Paddle__Environment` | `sandbox` or `production` | Phase 4 |
| `Billing__Paddle__ApiKey`, `__WebhookSecret`, `__ClientToken` | secrets | Phase 4 |
| `Seo__BaseUrl` | `https://myquickmenu.com` | Already required; verification links and Paddle return URLs depend on it |
| `Email__From` | an address on myquickmenu.com | Phase 2 |

**Deploy order.** Each phase's migration is additive and runs at startup. The slug backfill in phase 0 runs inside the migration and logs every renamed slug; check the log and the alias table on the first production deploy. Paddle's webhook URL is registered once phase 4 is in production, pointing to `https://myquickmenu.com/billing/webhooks/paddle`.

**Tests** (in `RestaurantMenu.Tests`, following the rules-in-helpers split):
- `SlugRules`: transliteration, reserved words, collisions, the apostrophe case
- `PricingRules`: totals for each preset, yearly discount, branch quantity, dependent modules (OwnDomain needs Website)
- `EntitlementRules` and `SubscriptionRules`: every lifecycle transition with a fixed clock, including the edge of each day count
- Webhook handling: signature failure rejected, duplicate event ignored, out-of-order events (a payment before the subscription-created event) handled
- `TranslationCoverageTests` extended to the new Albanian and English funnel text
- End to end in the running app at `localhost:5101` for each phase's acceptance check, Paddle in sandbox

---

## Risks and open items

| Risk or open item | Effect if ignored | What to do |
|---|---|---|
| Paddle may refuse or delay onboarding | Phase 4 slips | Apply as soon as the legal entity is known; bank transfer already covers payments; Lemon Squeezy as backup |
| Albanian customers may want a local fiscalized invoice even when paying Paddle | Customers ask for invoices you can't issue | Ask an accountant before phase 4; the Invoice table can issue a local invoice per Paddle payment if needed |
| Fiscalizing your own bank-transfer invoices | Invoices not valid for tax | Confirm with the accountant whether to integrate with the fiscal system or issue through accounting software and store the fiscal code |
| Slug backfill renames a live menu link | A printed QR stops working | Aliases keep every old link resolving; never delete an alias |
| Open signup brings spam accounts | Junk branches and wasted email quota | Approval mode at launch, honeypot, rate limits, unverified accounts deleted after 7 days |
| Shared Gemini key on the free tier | Translate-assist fails for everyone when one account uses it heavily | Per-account daily limit in `EntitlementRules`; move to a paid key when trials grow |
| SMS costs | Bills grow with bookings | SMS off by default for self-serve accounts until SMS packs exist |
| Legacy customers | Unhappy customers if moved without notice | Keep them on Legacy until the owner sets a date and sends notice |

**Still needed from the owner:** the operator's legal entity details and bank account, the price for each module (monthly and yearly), the seats included per branch, and confirmation of the defaults in the summary.

**Sources:** myquickmenu.com · Stripe in Albania (OneSafe) · Paddle supported sellers · Paddle selling countries · Paddle fees and payouts in practice · Lemon Squeezy payout countries · RaiAccept docs · Online payments in Albania (ueb.al)
