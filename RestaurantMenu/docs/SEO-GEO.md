# SEO and GEO for My Quick Menu

How My Quick Menu (marketing site and every restaurant page) is made findable in search engines (SEO)
and quotable by AI assistants such as ChatGPT, Perplexity, Claude, Gemini and Copilot (GEO, generative
engine optimisation). Built on branch `feat/seo-geo` (October 2026).

**Contents**

1. [What is where](#1-what-is-where)
2. [The head pipeline](#2-the-head-pipeline)
3. [Adding SEO to a new page](#3-adding-seo-to-a-new-page)
4. [Languages and URLs](#4-languages-and-urls)
5. [Indexing rules](#5-indexing-rules)
6. [Structured data (JSON-LD)](#6-structured-data-json-ld)
7. [Sitemaps, robots.txt, llms.txt](#7-sitemaps-robotstxt-llmstxt)
8. [Content pages and writing for AI answers](#8-content-pages-and-writing-for-ai-answers)
9. [How restaurants control their SEO](#9-how-restaurants-control-their-seo)
10. [IndexNow](#10-indexnow)
11. [AI referral tracking](#11-ai-referral-tracking)
12. [Performance](#12-performance)
13. [Custom domains](#13-custom-domains)
14. [Configuration](#14-configuration)
15. [Verifying](#15-verifying)
16. [Post-deploy checklist](#16-post-deploy-checklist)
17. [Open TODOs](#17-open-todos)

---

## 1. What is where

| Concern | Code |
|---|---|
| Head model and partial | `Models/SeoMetadata.cs`, `Views/Shared/_SeoMeta.cshtml` |
| URL builder (canonical, menu URLs) | `Services/SeoService.cs` |
| Language rules | `Helpers/SeoLanguages.cs` |
| Generated titles and descriptions | `Helpers/SeoText.cs`, `Helpers/Cuisines.cs` |
| Indexing rules | `Helpers/SeoIndexing.cs` |
| JSON-LD model and builder | `Helpers/JsonLd.cs`, `Helpers/StructuredData.cs`, `Services/RestaurantSeo.cs` |
| Sitemaps, robots.txt, llms.txt | `Services/SitemapService.cs`, `Controllers/SeoController.cs`, `Helpers/RobotsTxt.cs`, `Helpers/LlmsText.cs` |
| Change tracking (`UpdatedUtc`, cache invalidation, IndexNow trigger) | `Services/SeoChanges.cs` |
| Marketing pages registry and copy | `Helpers/MarketingPages.cs`, `Helpers/MarketingText.cs`, `Helpers/Landing/LandingContent*.cs` |
| Content pages and directory | `Controllers/LandingController.cs`, `Views/Landing/*`, `Views/Shared/_MarketingLayout.cshtml` |
| Brand facts (name, address, contact, profiles) | `Services/BrandOptions.cs`, `appsettings.json` → `Brand` |
| Restaurant SEO settings | `Controllers/SearchController.cs`, `Views/Search/Index.cshtml`, `Helpers/SearchRules.cs` |
| IndexNow | `Services/IndexNow.cs` |
| AI referrals | `Services/AiReferralMiddleware.cs`, `Helpers/AiReferrals.cs`, Admin → AI referrals |
| URL hygiene, ETag/304, HEAD | `Services/UrlHygiene.cs` |
| Image variants | `Services/ImageVariants.cs`, `Helpers/ImageUrls.cs` |
| Tools | `tools/og` (share images), `tools/screens` (product screenshots), `tools/icons` (icon subset), `tools/seo-crawl` (crawl report) |

## 2. The head pipeline

Every public page sets one `SeoMetadata` in its controller (`ViewData["Seo"]`). Its layout renders
it with `_SeoMeta`:

- `<title>`, meta description, robots (`index, follow, max-image-preview:large, max-snippet:-1` or
  `noindex, follow`; back-office screens `noindex, nofollow`);
- canonical (absolute, the page's own language, no tracking or UI parameters);
- hreflang for every language the page has, plus `x-default` (the main-language bare URL);
- Open Graph (`og:locale` as `sq_AL`/`en_US`/…, `og:locale:alternate`, image with size and alt) and
  Twitter card;
- one JSON-LD `@graph`;
- `<html lang>` comes from `SeoMetadata.Locale`;
- `LastModifiedUtc` becomes the `Last-Modified` header (`HtmlValidatorsMiddleware`).

`HtmlValidatorsMiddleware` adds a weak ETag (hash of the page, ignoring the menu's render time) and
`Cache-Control: no-cache` to menus, websites and marketing pages, and answers `304` when nothing
changed.

## 3. Adding SEO to a new page

1. Public marketing page: add it to `MarketingPages.All` (path, change frequency, languages). The
   sitemap, IndexNow, the ETag middleware and the tests pick it up from there.
2. In the controller, resolve the language and 301 a wrong `?lang=`:
   ```csharp
   var (language, wrong) = MarketingPages.Resolve(MarketingPages.Get("my-page"), lang);
   if (wrong) return RedirectPermanent("/my-page" + SeoLanguages.QueryWithout(Request.Query));
   ```
3. Set `ViewData["Seo"] = new SeoMetadata { Title, Description, CanonicalUrl, Alternates, XDefaultUrl, Locale, ImageUrl…, JsonLd }`.
   Keep the title at 60 characters or fewer and the description between 140 and 160
   (`SeoText.Trim` cuts at a word). Build the JSON-LD with `StructuredData.ForMarketingPage`; never
   concatenate JSON.
4. Use `_MarketingLayout` (or set `ViewData["Marketing"] = await _marketing.ChromeAsync(lang, path)`
   on the app layout) so the page gets the shared header, footer and NAP.
5. Add a share image to `tools/og/pages.json` and run `python3 tools/og/generate.py <key>`.
6. If the page name could clash with a restaurant link, add it to `SlugRules.Reserved`.
7. Run the tests and `tools/seo-crawl`.

## 4. Languages and URLs

- The URL scheme is unchanged: `?lang=xx`. Printed QR codes (`/menu/{slug}`, `?t=&k=`) keep working.
- Each branch has a **main language** (`Branch.DefaultLanguage`, set under Edit branch → Menu
  languages). The bare URL shows it. Every other language lives on `?lang=xx`.
- `?lang=<main>` and `?lang=<not offered>` are 301-redirected to the bare URL; a table's `?t=&k=`
  survives the redirect.
- Existing branches were given English where they offer it, otherwise their first language. No
  existing link changed language.
- The marketing site is English on the bare URL and Albanian on `?lang=sq`.
- Restaurant meta copy (titles, descriptions, booking page) is written in all 7 menu languages
  (`SeoText`), so an Albanian page never gets English meta. An owner's own title and description are
  used only in their own language (base text = English when English is offered, as for dishes).
- URL hygiene (301): trailing slash removed, upper case lowered (uploads and assets excepted),
  `/home` and `/home/index` → `/`. HTTPS and www → apex are already done by Render. With
  `Seo__RedirectRenderHost=true`, `*.onrender.com` also redirects to `Seo__BaseUrl` (off by default;
  switching it on changes the host setup).
- Unknown restaurants and pages answer a real 404 (noindex) with links to the directory and pricing.

## 5. Indexing rules

| Page | Indexed when | Otherwise |
|---|---|---|
| Menu `/menu/{slug}` | the owner's **Show my menu in Google** is on, the branch isn't marked **demo** by an admin, and it has at least one visible dish | `noindex, follow`, left out of the sitemaps |
| Website `/site/{slug}` | the same, and the website is published | the same |
| Booking `/book/{slug}` | never (a form, not content) | `noindex, follow` |
| Reservation links, signup, back office | never | `noindex` (back office also `nofollow` and `X-Robots-Tag`) |
| 404/error pages | never | `noindex, follow` |
| Marketing pages, directory | always (an empty directory is noindex) | |

Categories and dishes named like test data (`Test`, `Testing 2`, `asdf`, `Lorem ipsum`, `demo` …,
see `SeoText.IsPlaceholder`) are left out of descriptions, price ranges and structured data. The guest
menu still shows them.

Demo restaurants also stay off the home page's partner strip and the directory.

## 6. Structured data (JSON-LD)

Typed nodes (`Helpers/JsonLd.cs`) serialised with System.Text.Json into one `@graph` per page. HTML
is escaped (`< > & ' "` → `\uXXXX`), real characters are kept (`ë`, `€`), and nothing is ever
HTML-encoded twice.

Stable `@id`s:

- `https://myquickmenu.com/#organization`, `/#website`, `/#software`, `/#logo`
- `{restaurant home}#restaurant`: the published website's address if there is one, otherwise the
  bare menu URL. The **same id** appears on the menu (every language) and on the website, so
  search engines see one restaurant.

Per page type:

| Page | Nodes |
|---|---|
| Every marketing page | Organization (name, logo, description = the brand definition, email, phone, Tirana address, area served, contact point in sq/en, sameAs, parent 4CS), WebSite, WebPage (AboutPage, ContactPage, CollectionPage) |
| Home, pricing, product pages | + SoftwareApplication with one monthly Offer per plan **from the price book** |
| Non-home pages | + BreadcrumbList |
| Product pages, FAQ | + FAQPage (only questions whose answer is shown) |
| Directory | + ItemList of restaurants |
| Menu | Restaurant / CafeOrCoffeeShop / BarOrPub / Bakery / FastFoodRestaurant / IceCreamShop (address, geo, phone, cuisine, price range from real dish prices, opening hours grouped by day, `acceptsReservations` + ReserveAction when bookings are on, logo, image, hasMap, sameAs), Menu → MenuSection → MenuItem (description, photo, Offer with the menu's currency and InStock/SoldOut, suitableForDiet), BreadcrumbList, WebPage |
| Website | Restaurant (same id), WebPage |

Deliberately **not** included:

- `aggregateRating` and `review` from the "How was your visit?" feedback. Reviews a business collects
  about itself are not eligible on LocalBusiness and risk a manual action.
- Allergens. schema.org has no allergen property on MenuItem; they stay visible on the page.
- Booking "closed dates" as closed opening hours. They only stop online bookings and don't mean the
  restaurant is closed.

Snapshots of every page type are in `RestaurantMenu.Tests/Snapshots/*.json`. After an intended
change, run the tests with `UPDATE_SNAPSHOTS=1`, review the diff and commit it.

## 7. Sitemaps, robots.txt, llms.txt

- `/sitemap.xml` is a sitemap index of `/sitemaps/pages.xml` (marketing pages × languages),
  `/sitemaps/menus.xml` (indexable menus × languages with `lastmod` = last change to the branch, a
  category or a dish, and `image:image` for the banner and dish photos on the main-language URL) and
  `/sitemaps/sites.xml` (published websites without an address of their own). Every entry carries
  reciprocal `xhtml:link` hreflang plus x-default. Menus split into `menus-2.xml`… past 45,000 URLs.
  The result is cached for an hour and rebuilt as soon as a restaurant saves anything (`SeoChanges`).
- `/robots.txt` disallows the back office (`RobotsTxt.AppOnlyPaths`) for everyone and for a named
  group of AI search and assistant crawlers. Those tokens were checked against each vendor's docs on
  2026-10-11: GPTBot, OAI-SearchBot and ChatGPT-User (OpenAI); PerplexityBot and Perplexity-User;
  ClaudeBot, Claude-SearchBot and Claude-User (Anthropic); Google-Extended; Applebot and
  Applebot-Extended; Bingbot. The Sitemap line uses the request's host. Live, every one of these
  user agents gets a 200 (nothing upstream blocks them).
- A restaurant's own domain serves its own `/robots.txt` and `/sitemap.xml` (the website in each
  language).
- `/llms.txt` follows the llmstxt.org format: the brand definition, product pages, current prices,
  FAQ, about/contact, directory and the Albanian pages. `/llms-full.txt` is the full text of every
  content page in both languages. Both are generated from the same data as the pages.

## 8. Content pages and writing for AI answers

The product pages (`/digital-menu`, `/restaurant-website`, `/table-bookings`,
`/restaurant-management`), `/faq`, `/about` and `/contact` are data in
`Helpers/Landing/LandingContent.En.cs` and `.Sq.cs`, rendered by `Views/Landing/Page.cshtml`.

Rules for the copy:

- **Open with a quotable definition**, then the brand definition, word for word the same everywhere
  (`MarketingText.Definition`; `{definition}` in content).
- **Question-style H2s** that mirror how people ask assistants ("How much does a QR menu cost in
  Albania?").
- **Concrete facts only.** Prices and trial days come from the price book through tokens (`{menu}`,
  `{guests}`, `{everything}`, `{website}`, `{bookings}`, `{ordering}`, `{management}`, `{domain}`,
  `{trial}`). Never type a price into the copy.
- **Never invent numbers, customers, awards or testimonials.** An unknown fact is written as
  `[TODO: …]`. TODOs show in Development and are left out everywhere else; a paragraph or FAQ that
  is only a TODO disappears.
- **Short, self-contained paragraphs**, a comparison table per product page, 4–6 FAQs, and a
  visible "Last updated" date (`LandingContent.LastUpdated`; update it when you check the facts
  again).
- **Keep English and Albanian the same shape.** `LandingContentTests` checks that, plus meta
  lengths with real prices, tokens, links and word counts.

The Albanian copy is written for Albanian owners (formal "ju", *menu dixhitale*, *degë*,
*klientë*). Phrases worth a native read before launch are listed in the handover.

`/restaurants` lists every indexable restaurant by city with links to its menu, website and
booking page (60 per page, plain numbered links, CollectionPage + ItemList).

Internal links:

- The header has a Products menu (a `<details>`, so it works without JS).
- The footer links every page and repeats the name, address, email and phone.
- The home page's product tiles link to their pages.
- Every guest page ends with "Digital menu / Restaurant website / Online bookings by My Quick Menu"
  in the guest's language, linking to the matching product page (a normal followed link).

Share images (1200×630) for every marketing page: edit `tools/og/pages.json`, then run
`python3 tools/og/generate.py` (it needs Chrome). Product screenshots come from a local **demo**
restaurant via `node tools/screens/capture.mjs http://localhost:5099 <auth cookie>` (`shots.json`
lists them). Never capture real customers' data.

## 9. How restaurants control their SEO

**Branch → Search & sharing** (`/branch/{id}/search`, managers and owners; admins for any branch):

- **Show my menu in Google** (default on);
- type of place (Restaurant, Café, Bar, Bakery, Fast food, Ice cream: the schema.org type) and
  cuisine (14 options, translated into all 7 languages);
- city, and map location (paste "41.3275, 19.8187" from Google Maps);
- Google Business Profile link (validated to be a Google URL; given as `hasMap`);
- their own search title and description in the base language plus translations, with a live Google
  preview and character counters. Empty means the generated text, shown as the placeholder;
- link-preview image (1200×630 recommended; the banner otherwise, auto-cropped);
- admins only: **Demo or test restaurant**;
- the **Share & get found** checklist with a completeness score: address, city, phone,
  type/cuisine, hours, logo, banner, dish photos, map, Google Business Profile, website, social
  links, plus unscored tips (own domain, menu link in the Instagram bio). A compact version with
  the next three items sits on each branch page.

The main language is chosen under **Edit branch → Menu languages**.

## 10. IndexNow

- Set `IndexNow__Key` (8–128 letters, digits or dashes). The key file is answered at `/{key}.txt` on
  every host, including restaurants' own domains.
- `IndexNowWorker` sends to `https://api.indexnow.org/indexnow` (Bing, Yandex, Seznam, Naver;
  Bing's index feeds ChatGPT search and Copilot) every 30 seconds:
  - the menus and websites of branches saved since the last run (publish, menu edits, hours, slug
    changes, deletions), through `SeoChanges`;
  - the marketing pages once per start, which means once per deploy.
- Each URL is sent at most once per 10 minutes; a later change waits until the window ends. URLs are
  grouped by host.
- Off without a key, or when `Seo__BaseUrl` isn't a public https address. Logs say so at start.

## 11. AI referral tracking

There is no web analytics, so `AiReferralMiddleware` records public-page visits that come from
ChatGPT (chatgpt.com, chat.openai.com, `utm_source=chatgpt.com`), Perplexity, Claude, Gemini and
Copilot. It stores only the assistant, how it was recognised, the host, the path and the time: no
IP, browser or cookie. The row is written off the request path, and rows are deleted after
`Analytics:RetentionDays`. **Admin → AI referrals** shows visits per assistant, per day and per
landing page for 30, 90 or 365 days.

## 12. Performance

- Uploaded photos are served as WebP at the width needed (`/images/x.jpg?w=320…1920`, made on first
  request, cached on the persistent disk under `images/_cache`, never upscaled; widths are snapped
  to a fixed list and paths are confined to the uploads folder). Pages use `srcset`/`sizes`,
  width/height, `loading="lazy"` below the fold and `fetchpriority="high"` + preload for the banner.
  `?og=1` gives a 1200×630 JPEG crop for link previews. ImageSharp can't write AVIF, so there is no
  AVIF.
- Fonts are self-hosted (latin + latin-ext, `font-display: swap`). Phosphor icons are cut to the
  icons in use (`tools/icons/subset.py`; `IconSubsetTests` fails until you re-run it after adding an
  icon).
- Caching: uploads and `?v=` fingerprinted assets get `public, max-age=31536000, immutable`; fonts
  and `/img` a week; other static files an hour; HTML gets `no-cache` + ETag.
- Compression (Brotli) and HTTP/3 are done by Render's edge (verified on the live site). There is no
  Caddy in this deployment. The app adds `nosniff`, `Referrer-Policy`, frame rules (SecurityHeaders)
  and HSTS for a year.
- Lighthouse, mobile, local Debug build without edge compression, before → after:
  home 62 → 79, product page 63 → 93, menu 61 → 74, website 68 → 90; LCP 8.9 → 4.7 s,
  7.7 → 2.8 s, 11.1 → 5.3 s and 10.8 → 3.2 s; CLS ≤ 0.005 throughout. Production adds Brotli on top.
  Re-measure on the live site after deploy.

## 13. Custom domains

- A site's canonical is its own address: a verified own domain whose plan includes own domains,
  otherwise its free subdomain of `Sites__WildcardDomain`, otherwise `/site/{slug}`. Once a
  published site has its own address, `myquickmenu.com/site/{slug}` 301s to it.
- Menus keep their `myquickmenu.com/menu/{slug}` canonical even on an own domain. Printed QR codes
  point there, and a menu must not depend on a restaurant's DNS.
- `*.myquickmenu.com` subdomains need the wildcard DNS record and the Render wildcard domain first.
  Set `Sites__WildcardDomain=myquickmenu.com` only after `https://anything.myquickmenu.com` answers;
  until then canonicals would point at addresses that don't resolve.

## 14. Configuration

| Setting | Meaning |
|---|---|
| `Seo__BaseUrl` | Canonical origin, e.g. `https://myquickmenu.com`. Required in production. |
| `Seo__RedirectRenderHost` | `true` to 301 `*.onrender.com` to `Seo__BaseUrl` (default off). |
| `Brand__Email`, `Brand__Phone`, `Brand__WhatsApp`, `Brand__StreetAddress`, `Brand__PostalCode`, `Brand__SameAs__0…` | The brand's facts on every page and in the schema. |
| `IndexNow__Key` | Turns IndexNow on. |
| `Sites__WildcardDomain` | Free subdomains (see §13). |
| `Analytics__RetentionDays` | Also how long AI referral rows are kept. |

## 15. Verifying

- `dotnet test RestaurantMenu.Tests/RestaurantMenu.Tests.csproj`: languages, meta copy, indexing,
  robots, JSON-LD snapshots and escaping, sitemaps against the sitemaps.org and Google image XSDs,
  URL hygiene, HEAD, llms.txt, AI referrals, content completeness, icon subset.
- `python3 tools/seo-crawl/crawl.py https://myquickmenu.com` (or a local instance) fetches every
  sitemap URL and checks:
  - status codes, titles and descriptions (missing, duplicate, length);
  - canonical, `<html lang>`, and hreflang (in-page vs sitemap, and reciprocity);
  - JSON-LD present and parseable;
  - images without alt, H1 count and heading order;
  - noindexed URLs in the sitemap;
  - the redirects and 404s in its `REDIRECTS` list.

  It exits 1 on errors.
- **Rich Results Test** (https://search.google.com/test/rich-results), after deploy:
  - `https://myquickmenu.com/menu/olivers-italian`
  - `https://myquickmenu.com/site/tirana-palace`
  - `https://myquickmenu.com/faq`
  - `https://myquickmenu.com/pricing`
  - `https://myquickmenu.com/digital-menu?lang=sq`

  Before deploy, paste the JSON from `RestaurantMenu.Tests/Snapshots/menu-full.json` or `site.json`
  (Code tab).
- **Schema validator** (https://validator.schema.org): the same URLs. All page types returned
  0 errors and 0 warnings before release.
- **Google Search Console** and **Bing Webmaster Tools**: coverage, sitemaps, Core Web Vitals,
  hreflang.

## 16. Post-deploy checklist

- [ ] `Seo__BaseUrl=https://myquickmenu.com` is set on Render; deploy (the migrations
      `SearchAndSharing` and `AiReferrals` only add columns and a table).
- [ ] Set `IndexNow__Key` (any random 32-character string); check `https://myquickmenu.com/<key>.txt`.
- [ ] Fill `Brand__SameAs__*` (Instagram, Facebook, LinkedIn… of **My Quick Menu**),
      `Brand__StreetAddress` and `Brand__WhatsApp` if wanted, and the content TODOs (§17).
- [ ] Verify `myquickmenu.com` in **Google Search Console** and **Bing Webmaster Tools**; submit
      `https://myquickmenu.com/sitemap.xml` in both. Verify restaurants' own domains too if you
      manage them.
- [ ] Create or claim the **Google Business Profile** for My Quick Menu (Tirana) with the same name,
      address, phone and website as the footer.
- [ ] Mark demo/test restaurants as **Demo** (Branch → Search & sharing, as admin); check that
      Oliver's Italian has no "Testing" category left.
- [ ] Run `tools/seo-crawl` against production; fix anything it reports.
- [ ] Rich Results Test and schema validator on the URLs in §15.
- [ ] Lighthouse (mobile) on home, `/digital-menu`, a menu and a website; note the numbers here.
- [ ] Monthly: ask ChatGPT, Perplexity, Gemini and Claude, in English and Albanian, for example
      "best QR menu for restaurants in Albania", "menu dixhitale për restorante në Tiranë",
      "online table booking system Albania" and "program menaxhimi për restorante". Log whether My
      Quick Menu is mentioned or cited, next to Admin → AI referrals.

## 17. Open TODOs

Facts only you can supply. They are in the copy as `[TODO: …]`, visible in Development and hidden
in production until filled:

- `Brand__SameAs`: My Quick Menu's own social profiles (`appsettings.json`).
- Street address (contact page, footer, Organization schema): optional.
- Support hours and a WhatsApp number, if any (contact page, FAQ).
- The response time you can promise (FAQ call to action).
- Cancellation: notice period and how to cancel (FAQ), as in the terms.
- Card payment, once Paddle is live (FAQ "How do I pay?").
- About page: when and why 4CS started My Quick Menu, and who is on the team.
- Whether SMS confirmations and reminders are on in production (bookings page).
- Whether translation suggestions (`Translation__GeminiApiKey`) are on in production (digital menu
  page).
