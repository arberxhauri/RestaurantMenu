# Plan: the new back office (dashboard, stations, tables, menu, stock, reports)

Status: **draft for review**, nothing built yet. Written 23 November 2026.

Goal: turn today's back office (one "Dashboard" link that opens everything, plus Manage and Billing) into a
full restaurant workspace with a clear side panel, live stations (POS, kitchen, bar), a table floor plan,
and reports, in a much more polished design. The public guest menu, booking page and restaurant websites
are **not** part of this work and keep working as they are.

---

## 1. What exists today (and is reused)

| Area | Today | Reused as |
|---|---|---|
| Shell | `Views/Shared/_Layout.cshtml`: sidebar with Dashboard, Manage, Billing, Admin, Account. Bootstrap 5 + `tokens.css` (Herb & Paprika palette, light/dark, Geist/Bricolage fonts, Phosphor icons) | New app shell keeps the tokens, fonts and icons |
| Menu | `Category`, `Product` (options, allergens, diets, badges, schedules, translations, soft delete) | Menu → Items, Categories, Modifiers |
| Recipes | `DishIngredient` (product → ingredient quantities), stock is used automatically by orders | Menu → Recipes |
| Stock | `Ingredient` (unit, quantity, low level, cost), `StockMovement` (Delivery, Usage, **Waste**, Count, Sale, SaleReturn) | Stock → Items, Movements, Waste, Counts |
| Orders | `Order`/`OrderItem` (table orders from the guest menu), statuses New → Preparing → Served / Cancelled, SignalR `KitchenHub`, kitchen display | Stations → Kitchen, and the base for POS and Bar |
| Tables | `DiningTable` (number, name, QR code) | Tables → floor plan (extended) |
| Bookings | `Booking`, reservation settings, owner day view | Tables → Reservations |
| Sales | `DailySales` nightly roll-up, `SalesService` | Reports → Sales |
| Analytics | `MenuEvent`, `MenuInsights`, AI referrals | Reports → Menu analytics |
| Team | `BranchMember` with roles Editor (waiter) and Manager, `Shift` (with a free-text station) | Configuration → Users & roles, Shifts |
| Billing | Plans, modules (`Menu`, `Ordering`, `Bookings`, `Website`, `Management`, `OwnDomain`), Paddle | Configuration → Billing; modules gate the new sections |

Nothing here is thrown away; most of the work is a new shell and information architecture, three new
live screens (POS, bar, floor plan), new data for areas/discounts/payments/waste reasons, and reports.

---

## 2. Information architecture (the side panel)

```
◉ Dashboard
▤ Reports          Sales · Items & categories · Hours & days · Payments · Waste & food cost
                   · Staff & shifts · Menu analytics · Feedback · Bookings
◈ Stations         POS · Kitchen · Bar
▦ Tables           Floor plan (live) · Reservations · Areas & layout · QR codes
☰ Menu             Items · Categories · Modifiers · Recipes · Discounts & happy hours
                   · Availability & schedules · Translations
⬚ Stock            Items · Categories · Movements · Waste · Stock counts · Suppliers (later)
⚙ Configuration    Business overview · Branches · Users & roles · Shifts · Settings
                   (ordering, bookings, receipts, taxes) · Website & domains · Billing
```

- Groups collapse; the active group opens by itself. On tablets the panel becomes an icon rail; on phones
  an off-canvas drawer (as today).
- **Top bar**: branch switcher (owners with several branches), global search / command palette (Ctrl/⌘ K:
  jump to any page, dish, table, order), live indicators (new orders, waiting tables), notifications
  (low stock, new booking, low rating, billing), user menu (account, language, sign out).
- A section the account's plan doesn't include stays visible with a small lock and opens an upsell page
  (today's `_PlanNotice` behaviour), so owners discover features.
- Admin pages (platform admin) stay in their own group, shown only to admins.

---

## 3. Design direction

Inspired by the reference shot's **layout patterns** (not its branding or artwork): calm light surfaces,
generous spacing, a slim labelled sidebar, cards with soft borders, colour used only for status.

- **Brand**: keep My Quick Menu's own palette and type from `tokens.css` (orange accent, stone-green
  neutrals, Bricolage Grotesque headings, Geist body, Phosphor icons). Dark mode is first-class, which
  matters for kitchen and bar screens.
- **Status colours** (one meaning everywhere, also shown with an icon/label, never colour alone):
  free / available · reserved · seated / in progress · needs attention (waiting long, bill requested) ·
  done / paid · cancelled.
- **Components** (one small library, documented on a hidden `/_styleguide` page): app shell, page header
  with actions, KPI tile with trend, chart card, data table (sort, filter chips, search, pagination,
  empty/loading states, CSV export), status chip, segmented tabs with counts, drawer (side panel forms),
  modal, toast with undo (exists), stepper (+/−), number pad (POS), order ticket card, table shape.
- **Density modes**: "office" (owners at a desk: tables, charts) and "station" (touch: large targets
  ≥ 48 px, big type, no hover-only actions) for POS, kitchen and bar.
- **Accessibility**: WCAG 2.2 AA contrast in both themes, full keyboard use, focus states, screen-reader
  labels for status, reduced-motion support.
- **Performance**: server-rendered Razor pages stay fast; live screens use the existing SignalR. Charts
  use one small self-hosted library (e.g. uPlot or Chart.js), loaded only on report pages, so the privacy
  page stays true ("typefaces and icons are served by us").

---

## 4. The sections in detail

### 4.1 Dashboard
Today for the selected branch (or all branches):
- KPI row: sales today vs same weekday last week, orders, covers, average ticket, tables in use, open
  bookings tonight.
- Live strip: orders waiting per station, oldest ticket age, tables needing attention.
- Charts: sales by hour (today vs average), top 5 dishes, sales last 14 days.
- Alerts: low stock items, items marked unavailable, low ratings since yesterday, billing notices.
- Quick actions: new POS order, mark dish unavailable, add booking, stock delivery.
Merges today's `Dashboard` and `Manage` overview.

### 4.2 Stations
**POS** (new; staff take orders and close bills)
- Layout like the reference: order type and status tabs with counts (dine-in, takeaway; open, ready,
  paid), ticket cards; centre dish grid with category chips, search and +/−; right panel with the
  current ticket (table, guests, items with options and notes, discounts, subtotal, tax, total).
- Actions: send to stations (kitchen/bar split automatically), add to an open table ticket, split /
  move / merge tables, apply discount (role-limited), request bill, **record payment** (cash, card,
  mixed, with change), reprint / print bill, void items (manager PIN, reason logged).
- Guest QR orders and POS orders are the same `Order` and appear in the same lists.
- Works offline for a short drop (queue in the browser, the same idea as the guest menu's offline mode).

**Kitchen** and **Bar** (kitchen display exists; bar is new)
- Each product (or category, as a default) gets a **station**: Kitchen, Bar, or both/none. An order is
  split into per-station tickets; each station sees only its items, live over SignalR.
- Ticket timer with colour as it ages, bump (preparing → ready), recall, all-day counts ("7 × margarita"),
  sound on new ticket, full-screen station mode for a wall tablet.
- When every station has marked its part ready, the POS / waiter sees "ready to serve".

### 4.3 Tables
- **Floor plan (live)**: areas as tabs (e.g. Main dining, Terrace, Bar), tables drawn with shape and
  seats, colour by status (free, reserved soon, seated, waiting for food, bill requested), guests and time
  seated on each. Tap a table: open its ticket in POS, seat a booking, move or merge, print its QR.
- **Reservations**: today's list next to the plan (as in the reference), linked to the existing bookings
  (seat a booking on a table, no-show, walk-in).
- **Areas & layout editor**: drag tables on a grid, set shape (round, square, rectangle), seats, area,
  name; QR codes stay per table (existing codes never change, so printed QRs keep working).

### 4.4 Menu
Items, categories, modifiers (today's option groups), **recipes** (today's dish ingredients, with cost per
dish and margin), **discounts & happy hours** (new: percentage / amount, on items or the bill, time
windows, POS-only or also on the guest menu), availability & schedules (exist), translations (exist).
Bulk actions: mark unavailable, move category, change price by %. Station assignment per item/category.

### 4.5 Stock
Items (ingredients), **stock categories** (new: e.g. Drinks, Dairy, Meat), movements (exists), **waste**
(new screen on top of `StockMovementKind.Waste`, with reasons: expired, damaged, prep loss, returned;
cost of waste), stock counts (exists, made faster for a tablet walk-through), low-stock alerts, later
suppliers and purchase orders.

### 4.6 Reports
Date range + branch filters, compare to previous period, CSV/PDF export:
sales (by day, hour, weekday), items and categories (quantity, revenue, margin from recipes), payments
(cash/card, discounts, voids), waste & food cost, staff & shifts (hours, sales per server once POS
records the server), menu analytics (exists), feedback (exists), bookings (no-shows, covers).

### 4.7 Configuration
Business overview (name, logo, contact, legal details), branches, **users & roles** (see §5), shifts,
settings (ordering, bookings, stations, receipts, tax rates, service charge), website & domains (exist),
billing (exists).

---

## 5. Roles and permissions

Today: Owner, plus per-branch Editor (waiter) and Manager. Proposed per-branch roles:

| Role | Can |
|---|---|
| Owner | Everything, all branches, billing, team |
| Manager | Everything in the branch except billing and deleting the branch; voids/discounts above the limit |
| Waiter | POS (own tables), floor plan, reservations, mark dishes unavailable |
| Cashier | POS, payments, bills, end-of-day cash count |
| Kitchen | Kitchen station only |
| Bar | Bar station only |
| Stock | Stock items, deliveries, counts, waste |

Stored as a role per branch member (existing Editor → Waiter, Manager → Manager, so no one loses access).
Station screens can run on a shared tablet with a **station login** (device token, no personal password).

---

## 6. Data changes (EF Core migrations)

New: `TableArea`; `DiningTable` + `AreaId`, `Shape`, `Seats`, `X`, `Y`, `Rotation`; `Station` enum on
`Product`/`Category`; `OrderTicket` (order × station, status, times); `Order` + `Source` (guest QR / POS),
`Type` (dine-in / takeaway), `Guests`, `ServerUserId`, `BillRequestedUtc`, `ClosedUtc`;
`Payment` (order, method, amount, tip, user, time); `Discount` + `OrderDiscount`; `StockCategory`;
`StockMovement` + `WasteReason`; `BranchMemberRole` values for the new roles; `StationDevice` (device
login). All additive; existing orders, tables and QR codes stay valid. Reports read from these plus the
existing `DailySales`.

---

## 7. Plans and pricing (decision needed)

POS, bar station and the floor plan fit the existing **Ordering** module; reports, stock categories and
waste fit **Management**. Option: a new **POS** module (priced separately) for POS + payments + cash
reports, leaving Ordering as guest QR ordering + kitchen display. Prices are set in Admin → Plans & prices,
and Paddle products would be added for a new module.

---

## 8. Important: fiscal receipts in Albania

Recording **payments** in a POS can make the system a cash register under Albania's fiscalisation law
(every cash/card sale needs a fiscalised receipt with NSLF/NIVF codes through the tax authority's
system). Until an accountant confirms the approach, the POS in this plan **records payments for the
restaurant's own reports and prints a pro-forma bill, not a fiscal receipt**, and says so on screen. A
certified fiscal integration (or a hand-off to the restaurant's existing fiscal device) is a separate,
later project.

---

## 9. Phases (each one ships on its own)

| Phase | Scope | Notes |
|---|---|---|
| 1. Shell & design system | New layout, grouped side panel, top bar (branch switcher, search, notifications), component library, style guide, dark mode, all existing pages moved into the new structure | No new features; every current page keeps working, URLs redirect |
| 2. Dashboard | New KPIs, charts, alerts, quick actions | Replaces Dashboard + Manage overview |
| 3. Menu & stock | Menu items/categories/modifiers/recipes in the new UI, station per item, discounts; stock categories, waste screen with reasons | Discounts used by POS later |
| 4. Tables | Areas, floor-plan editor, live floor plan, reservations side list | Builds on DiningTable + bookings |
| 5. Stations | Order tickets per station, kitchen display redesign, new bar display, station logins | Uses existing SignalR hub |
| 6. POS | Order taking, table tickets, discounts, payments, bills, cash report | Fiscal note (§8) |
| 7. Reports | All reports with filters, compare, export | Uses data from phases 3–6 |
| 8. Roles | New roles and permissions, station devices | Can start alongside 5 |

Every phase: tests first for the rules (pricing, ticket splitting, permissions, reports), the existing 491
tests stay green, deploy with `./deploy.sh`, checked on a tablet and a phone in light and dark mode.

---

## 10. Questions before phase 1

1. **POS payments**: record payments only (pro-forma bill), or do you already need fiscal receipts? Does
   the restaurant have a fiscal device or software today?
2. **Pricing**: POS inside the Ordering module, or a new POS module?
3. **Devices**: what will stations run on (tablets, which size; phones for waiters; a receipt printer)?
4. **Languages**: back office in English and Albanian (today mostly English)?
5. **Order types**: dine-in and takeaway only, or delivery too?
6. **Order of phases**: is the order in §9 right, or should tables/POS come first?
