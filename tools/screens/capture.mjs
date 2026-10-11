#!/usr/bin/env node
// Product screenshots for the marketing pages (wwwroot/img/screens), taken from a running app
// with headless Chrome over the DevTools protocol. No npm packages: Node 22+ (built-in WebSocket).
//
//   node tools/screens/capture.mjs http://localhost:5099 [cookie]
//
// [cookie] is the value of the .AspNetCore.Identity.Application cookie of an owner of the demo
// branch, for the back-office shots (kitchen display). Shots are listed in shots.json; run it
// against a local demo restaurant, never real customers' data.
import { spawn } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const base = (process.argv[2] || 'http://localhost:5099').replace(/\/$/, '');
const cookie = process.argv[3];
const shots = JSON.parse(readFileSync(process.env.SHOTS_FILE || join(here, 'shots.json'), 'utf8'));
const out = process.env.SHOTS_OUT || join(here, '..', '..', 'RestaurantMenu', 'wwwroot', 'img', 'screens');
const chrome = process.env.CHROME || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const port = 9333;
const sleep = ms => new Promise(r => setTimeout(r, ms));

const profile = mkdtempSync(join(tmpdir(), 'mqm-shots-'));
const proc = spawn(chrome, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`, 'about:blank'], { stdio: 'ignore' });

let ws, nextId = 1;
const pending = new Map(), listeners = [];
function send(method, params = {}) {
    const id = nextId++;
    ws.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}
function once(method) { return new Promise(r => listeners.push({ method, r })); }

try {
    let target;
    for (let i = 0; i < 100 && !target; i++) {
        try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); }
        catch { await sleep(100); }
    }
    ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise(r => ws.addEventListener('open', r));
    ws.addEventListener('message', e => {
        const m = JSON.parse(e.data);
        if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(m.error.message)) : p.resolve(m.result); }
        else if (m.method) for (const l of listeners.splice(0)) { if (l.method === m.method) l.r(m.params); else listeners.push(l); }
    });
    await send('Page.enable');
    await send('Network.enable');
    if (cookie) await send('Network.setCookie', { name: '.AspNetCore.Identity.Application', value: cookie, url: base });

    for (const s of shots) {
        await send('Emulation.setDeviceMetricsOverride', { width: s.width, height: s.height, deviceScaleFactor: s.scale || 1, mobile: !!s.mobile });
        if (s.colorScheme) await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: s.colorScheme }] });
        const loaded = once('Page.loadEventFired');
        await send('Page.navigate', { url: base + s.path });
        await loaded;
        await sleep(s.wait || 900);
        if (s.script) { await send('Runtime.evaluate', { expression: s.script }); await sleep(s.after || 700); }
        const shot = await send('Page.captureScreenshot', { format: 'webp', quality: 82, captureBeyondViewport: false });
        writeFileSync(join(out, s.name + '.webp'), Buffer.from(shot.data, 'base64'));
        console.log('wrote', s.name + '.webp', `${s.width * (s.scale || 1)}x${s.height * (s.scale || 1)}`);
    }
} finally {
    try { ws?.close(); } catch { }
    proc.kill();
    await sleep(300);
    try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch { /* Chrome may still be closing */ }
}
