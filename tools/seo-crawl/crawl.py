#!/usr/bin/env python3
"""SEO crawl of a running My Quick Menu (local or live). Python 3 standard library only.

    python3 tools/seo-crawl/crawl.py http://localhost:5099
    python3 tools/seo-crawl/crawl.py https://myquickmenu.com --max 300

Reads /sitemap.xml (and its child sitemaps), fetches every URL and reports:
status codes, missing/duplicate/too long titles and descriptions, canonical mismatches,
non-reciprocal hreflang, pages without (or with invalid) JSON-LD, images without alt, H1 count and
skipped heading levels, and noindexed pages that are in the sitemap. Also checks the redirects and
status codes in REDIRECTS below. Exit code 1 when anything is an error. See docs/SEO-GEO.md.
"""
import argparse, json, re, sys, urllib.request, urllib.error, xml.etree.ElementTree as ET
from html.parser import HTMLParser
from collections import defaultdict

SM = "{http://www.sitemaps.org/schemas/sitemap/0.9}"
XHTML = "{http://www.w3.org/1999/xhtml}"

# (path, expected status, expected Location ending) - checked on every run.
REDIRECTS = [
    ("/home/index", 301, "/"),
    ("/?lang=en", 301, "/"),
    ("/?lang=xx", 301, "/"),
    ("/pricing/", 301, "/pricing"),
    ("/Pricing", 301, "/pricing"),
    ("/faq?lang=en", 301, "/faq"),
    ("/restaurants?page=1", 301, "/restaurants"),
    ("/menu/this-restaurant-does-not-exist", 404, None),
    ("/this-page-does-not-exist", 404, None),
    ("/robots.txt", 200, None),
    ("/llms.txt", 200, None),
    ("/llms-full.txt", 200, None),
]

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None

OPENER = urllib.request.build_opener(NoRedirect)

def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "MQM-SEO-crawl/1.0", "Accept-Language": "en"})
    try:
        r = OPENER.open(req, timeout=30)
        return r.status, dict(r.headers), r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read().decode("utf-8", "replace") if e.fp else ""

class Page(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.title = None; self._in_title = False; self.meta = {}; self.links = []; self.ld = []; self._in_ld = False
        self.imgs_no_alt = []; self.headings = []; self.html_lang = None; self._buf = ""
    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "html": self.html_lang = a.get("lang")
        elif tag == "title": self._in_title = True; self.title = ""
        elif tag == "meta":
            key = a.get("name") or a.get("property")
            if key and key not in self.meta: self.meta[key] = a.get("content", "")
        elif tag == "link": self.links.append(a)
        elif tag == "script" and a.get("type") == "application/ld+json": self._in_ld = True; self._buf = ""
        elif tag == "img":
            # alt="" is fine for decorative images; a missing alt is not. Images filled by JS (no src) are skipped.
            if "alt" not in a and a.get("src"): self.imgs_no_alt.append(a.get("src"))
        elif re.fullmatch(r"h[1-6]", tag): self.headings.append(int(tag[1]))
    def handle_endtag(self, tag):
        if tag == "title": self._in_title = False
        elif tag == "script" and self._in_ld: self._in_ld = False; self.ld.append(self._buf)
    def handle_data(self, data):
        if self._in_title: self.title += data
        if self._in_ld: self._buf += data

def sitemap_urls(base):
    urls, todo = [], [base + "/sitemap.xml"]
    while todo:
        status, _, body = fetch(todo.pop(0))
        if status != 200: print(f"ERROR sitemap {status}"); continue
        root = ET.fromstring(body.encode())
        if root.tag == SM + "sitemapindex":
            todo += [s.find(SM + "loc").text for s in root.findall(SM + "sitemap")]
        else:
            for u in root.findall(SM + "url"):
                alts = {l.get("hreflang"): l.get("href") for l in u.findall(XHTML + "link")}
                urls.append((u.find(SM + "loc").text, alts))
    return urls

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("base")
    ap.add_argument("--max", type=int, default=1000)
    args = ap.parse_args()
    base = args.base.rstrip("/")
    errors, warnings = [], []
    def err(u, m): errors.append(f"{u}: {m}")
    def warn(u, m): warnings.append(f"{u}: {m}")

    for path, want, location in REDIRECTS:
        status, headers, _ = fetch(base + path)
        loc = headers.get("Location", "")
        if status != want or (location and not loc.endswith(location)):
            err(path, f"expected {want} {location or ''}, got {status} {loc}")

    pages = sitemap_urls(base)[: args.max]
    print(f"{len(pages)} URLs in the sitemaps")
    titles, descs, hreflangs = defaultdict(list), defaultdict(list), {}
    for url, sitemap_alts in pages:
        status, headers, body = fetch(url)
        if status != 200: err(url, f"status {status}"); continue
        p = Page(); p.feed(body)
        robots = p.meta.get("robots", "") + " " + headers.get("X-Robots-Tag", "")
        if "noindex" in robots: err(url, "noindex but listed in the sitemap")
        if not p.title: err(url, "no <title>")
        else:
            titles[p.title.strip()].append(url)
            if len(p.title) > 65: warn(url, f"title {len(p.title)} chars")
        d = p.meta.get("description", "")
        if not d: err(url, "no meta description")
        else:
            descs[d].append(url)
            if len(d) > 165: warn(url, f"description {len(d)} chars")
            if len(d) < 70: warn(url, f"description only {len(d)} chars")
        canonical = next((l.get("href") for l in p.links if l.get("rel") == "canonical"), None)
        if canonical != url: err(url, f"canonical is {canonical}")
        alts = {l.get("hreflang"): l.get("href") for l in p.links if l.get("rel") == "alternate" and l.get("hreflang")}
        hreflangs[url] = alts
        if sitemap_alts and {k: v for k, v in alts.items()} != sitemap_alts:
            err(url, "hreflang in the page and in the sitemap differ")
        lang = p.html_lang or ""
        if alts and alts.get(lang) != url: err(url, f'<html lang="{lang}"> but hreflang {lang} is {alts.get(lang)}')
        if not p.ld: err(url, "no JSON-LD")
        for block in p.ld:
            try: json.loads(block)
            except ValueError as e: err(url, f"invalid JSON-LD: {e}")
        if p.imgs_no_alt: err(url, f"{len(p.imgs_no_alt)} image(s) without alt, e.g. {p.imgs_no_alt[0][:80]}")
        h1 = p.headings.count(1)
        if h1 != 1: err(url, f"{h1} <h1> elements")
        for prev, cur in zip(p.headings, p.headings[1:]):
            if cur > prev + 1: warn(url, f"heading jumps from h{prev} to h{cur}"); break

    # hreflang must be reciprocal: every alternate names this page back.
    for url, alts in hreflangs.items():
        for lang, other in alts.items():
            if lang == "x-default" or other == url: continue
            back = hreflangs.get(other)
            if back is None: warn(url, f"hreflang {lang} -> {other} is not in the sitemaps")
            elif url not in back.values(): err(url, f"hreflang {lang} -> {other} doesn't link back")
    for t, us in titles.items():
        if len(us) > 1: warn(us[0], f"title shared by {len(us)} pages: {t[:60]}")
    for d, us in descs.items():
        if len(us) > 1: warn(us[0], f"description shared by {len(us)} pages")

    for w in warnings: print("WARN ", w)
    for e in errors: print("ERROR", e)
    print(f"\n{len(pages)} pages, {len(errors)} error(s), {len(warnings)} warning(s)")
    sys.exit(1 if errors else 0)

if __name__ == "__main__":
    main()
