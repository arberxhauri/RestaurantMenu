/* My Quick Menu: offline-ready guest menus.
   Registered by menu.js with scope "/menu", so it only ever controls guest menu pages;
   the back office, kitchen, bookings and websites are never touched.

   - Menu pages: network first, so guests see today's prices. On weak Wi-Fi (no answer in
     3.5 s) or offline, the last saved copy of that menu is shown, and the network answer,
     when it comes, refreshes the copy for next time.
   - CSS, JS, fonts and icons: from the cache at once, refreshed in the background.
   - Dish images: from the cache when there, else the network (then kept). The page also
     tells this worker about images further down, so they're kept before the guest scrolls.
   - Anything else (orders, feedback, bookings, order status): straight to the network.
   Served with Cache-Control: no-cache, so a new version here is picked up on the next visit;
   bump VERSION to drop every old cache. */
'use strict';

const VERSION = 'v1';
const PAGES = `mqm-pages-${VERSION}`;
const ASSETS = `mqm-assets-${VERSION}`;
const IMAGES = `mqm-images-${VERSION}`;
const MAX_PAGES = 30;
const MAX_IMAGES = 250;
const MAX_ASSETS = 80;
const NETWORK_TIMEOUT = 3500;
const ASSET_HOSTS = ['fonts.googleapis.com', 'fonts.gstatic.com', 'unpkg.com'];

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => {
    event.waitUntil((async () => {
        const keep = [PAGES, ASSETS, IMAGES];
        for (const key of await caches.keys()) {
            if (key.startsWith('mqm-') && !keep.includes(key)) await caches.delete(key);
        }
        await self.clients.claim();
    })());
});

function isMenuPage(url) {
    const p = url.pathname;
    if (p === '/menu' || p === '/menu/') return true;
    // /menu/{slug} only: not the endpoints under /menu (event, order, orders, feedback) or a manifest.
    return /^\/menu\/[^/]+\/?$/.test(p) && !/^\/menu\/(event|order|orders|feedback)\/?$/.test(p);
}

function isAsset(url) {
    if (url.origin === self.location.origin) {
        return /^\/(css|js|lib)\//.test(url.pathname) || url.pathname === '/favicon.ico' || url.pathname === '/logo.png'
            || /\/manifest\.webmanifest$/.test(url.pathname);
    }
    return ASSET_HOSTS.includes(url.hostname);
}

function isImage(request, url) {
    return url.origin === self.location.origin
        && (request.destination === 'image' || /^\/(images|img)\//.test(url.pathname));
}

self.addEventListener('fetch', (event) => {
    const request = event.request;
    if (request.method !== 'GET') return;
    const url = new URL(request.url);

    if (request.mode === 'navigate') {
        if (url.origin === self.location.origin && isMenuPage(url)) event.respondWith(page(event));
        return;
    }
    if (isAsset(url)) { event.respondWith(staleWhileRevalidate(event, ASSETS, MAX_ASSETS)); return; }
    if (isImage(request, url)) { event.respondWith(cacheFirst(event, IMAGES, MAX_IMAGES)); return; }
    // Everything else: the browser's normal network request.
});

async function page(event) {
    const request = event.request;
    const cache = await caches.open(PAGES);
    const network = fetch(request).then(async (response) => {
        // Only real menu pages are kept: not redirects, errors or other origins.
        if (response.ok && response.type === 'basic' && !response.redirected) {
            await cache.put(request, response.clone());
            await trim(PAGES, MAX_PAGES);
        }
        return response;
    });
    event.waitUntil(network.catch(() => undefined)); // let a late answer still refresh the copy

    const saved = () => cache.match(request).then((hit) => hit || cache.match(request, { ignoreSearch: true }));
    const timeout = new Promise((resolve) => setTimeout(resolve, NETWORK_TIMEOUT, 'timeout'));
    try {
        const first = await Promise.race([network, timeout]);
        if (first !== 'timeout') return first;
        // Slow network: the saved copy now, if there is one; otherwise keep waiting.
        return (await saved()) || (await network);
    } catch (e) {
        return (await saved()) || offlinePage();
    }
}

async function staleWhileRevalidate(event, name, max) {
    const cache = await caches.open(name);
    // ignoreVary: a copy the worker fetched itself (see warm) still matches the page's request.
    const hit = await cache.match(event.request, { ignoreVary: true });
    const network = fetch(event.request).then(async (response) => {
        // Opaque: cross-origin font/icon files without CORS; still fine to keep and replay.
        if (response.ok || response.type === 'opaque') {
            await cache.put(event.request, response.clone());
            await trim(name, max);
        }
        return response;
    }).catch(() => null);
    event.waitUntil(network);
    return hit || (await network) || Response.error();
}

async function cacheFirst(event, name, max) {
    const cache = await caches.open(name);
    const hit = await cache.match(event.request);
    if (hit) return hit;
    try {
        const response = await fetch(event.request);
        if (response.ok) event.waitUntil(cache.put(event.request, response.clone()).then(() => trim(name, max)));
        return response;
    } catch (e) {
        return Response.error();
    }
}

/** Keeps the newest entries: the cache's keys are in the order they were added. */
async function trim(name, max) {
    const cache = await caches.open(name);
    const keys = await cache.keys();
    for (let i = 0; i < keys.length - max; i++) await cache.delete(keys[i]);
}

/* The page sends { type: 'warm', urls: [...], assets: [...] }:
   - urls: images on the menu the guest may not have scrolled to yet (same-origin only);
   - assets: the styles, scripts and fonts it loaded before this worker was in charge.
   A few at a time, skipping what's already kept. */
self.addEventListener('message', (event) => {
    const data = event.data || {};
    if (data.type !== 'warm') return;
    const parse = (list) => (Array.isArray(list) ? list.slice(0, 120) : [])
        .map((u) => { try { return new URL(u, self.location.origin); } catch (e) { return null; } })
        .filter(Boolean);
    const images = parse(data.urls).filter((u) => u.origin === self.location.origin);
    const assets = parse(data.assets).filter((u) => (u.protocol === 'https:' || u.origin === self.location.origin) && isAsset(u));
    event.waitUntil((async () => {
        await warm(IMAGES, images, MAX_IMAGES, false);
        await warm(ASSETS, assets, MAX_ASSETS, true);
    })());
});

async function warm(name, urls, max, alsoFonts) {
    if (!urls.length) return;
    const cache = await caches.open(name);
    const queue = [];
    for (const u of urls) if (!(await cache.match(u.href))) queue.push(u);
    const kept = [];
    async function worker() {
        while (queue.length) {
            const u = queue.shift();
            try {
                const same = u.origin === self.location.origin;
                const response = await fetch(u.href, same ? { credentials: 'same-origin' } : { mode: 'cors', credentials: 'omit' });
                if (response.ok) { await cache.put(u.href, response.clone()); kept.push({ u, response }); }
            } catch (e) { /* offline or gone: skip */ }
        }
    }
    await Promise.all([worker(), worker(), worker(), worker()]);
    // Font stylesheets (Google Fonts) name the font files: keep those too.
    if (alsoFonts) {
        const fonts = [];
        for (const { u, response } of kept) {
            if (!ASSET_HOSTS.includes(u.hostname) || !/css/.test(response.headers.get('content-type') || '')) continue;
            const css = await response.text();
            // Only the alphabets the menu's languages use (each block is marked /* latin */ etc.).
            for (const block of css.matchAll(/\/\*\s*([\w-]+)\s*\*\/\s*@font-face\s*{([^}]*)}/g)) {
                if (!['latin', 'latin-ext'].includes(block[1])) continue;
                const m = /url\((['"]?)(https:\/\/[^)'"]+)\1\)/.exec(block[2]);
                try { const f = m && new URL(m[2]); if (f && isAsset(f) && !fonts.some((x) => x.href === f.href)) fonts.push(f); } catch (e) { }
            }
        }
        await warm(name, fonts.slice(0, 40), max, false);
    }
    await trim(name, max);
}

function offlinePage() {
    const html = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Offline</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#F4F4F2;color:#161616;font:16px/1.5 system-ui,-apple-system,sans-serif;padding:24px;text-align:center}
main{max-width:420px}h1{font-size:22px;margin:0 0 8px}p{margin:0 0 6px;color:#4A4A47}button{margin-top:16px;padding:12px 22px;border-radius:999px;border:0;background:#161616;color:#fff;font:600 15px system-ui,sans-serif}</style></head>
<body><main><h1>You're offline</h1><p>This menu hasn't been opened on this phone before, so there's no saved copy yet. Connect to Wi-Fi or mobile data and try again.</p>
<p lang="sq">Jeni offline. Lidhuni me internetin dhe provoni përsëri.</p><button onclick="location.reload()">Try again</button></main></body></html>`;
    return new Response(html, { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } });
}
