/* Kitchen display: table orders live from KitchenHub (SignalR), changes through
   KitchenController. The server is the referee: every button waits for its answer, and
   the full list is reloaded on start, after every reconnect and every 30 seconds, so a
   dropped push can never leave the screen wrong for long. All text from orders is set
   with textContent (dish names and notes come from guests and owners). */
(function () {
    'use strict';

    var body = document.body;
    var ORDERS_URL = body.dataset.ordersUrl;
    var STATUS_URL = body.dataset.statusUrl;
    var PAUSE_URL = body.dataset.pauseUrl;
    var HUB_URL = body.dataset.hubUrl;
    var BRANCH = Number(body.dataset.branch);
    var CURRENCY = body.dataset.currency || '';
    var TITLE = body.dataset.title || document.title;
    var TOKEN = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';

    var LATE_NEW = 5, LATE_PREPARING = 20; // minutes before a card is flagged
    var orders = {};   // id -> order
    var fresh = {};    // id -> true while highlighted as just arrived
    var loadedOnce = false;
    var denied = false;

    var money = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    function $(sel) { return document.querySelector(sel); }
    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text != null) e.textContent = text;
        return e;
    }
    function icon(name) { var i = el('i', 'ph ' + name); i.setAttribute('aria-hidden', 'true'); return i; }

    var toastTimer;
    function toast(text) {
        var t = $('[data-toast]');
        t.textContent = text;
        t.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { t.hidden = true; }, 4000);
    }

    /* ---------- Time ---------- */
    var serverOffset = 0; // server clock minus this screen's clock, from each full load
    function now() { return Date.now() + serverOffset; }
    function minutesSince(iso) { return Math.max(0, Math.floor((now() - new Date(iso).getTime()) / 60000)); }
    function ago(iso) {
        var m = minutesSince(iso);
        if (m < 1) return 'just now';
        if (m < 60) return m + ' min';
        return Math.floor(m / 60) + ' h ' + (m % 60) + ' min';
    }
    function clock(iso) {
        return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    /* ---------- Rendering ---------- */
    function column(o) {
        return o.status === 'new' ? 'new' : o.status === 'preparing' ? 'preparing' : 'done';
    }

    // Buttons per status: [label, icon, target status, style]
    var ACTIONS = {
        'new': [['Start', 'ph-cooking-pot', 'preparing', 'primary'], ['Served', 'ph-check', 'served', 'line']],
        'preparing': [['Served', 'ph-check', 'served', 'primary'], ['Back to new', 'ph-arrow-u-up-left', 'new', 'line']],
        'served': [['Undo', 'ph-arrow-u-up-left', 'preparing', 'line']],
        'cancelled': [['Restore', 'ph-arrow-u-up-left', 'new', 'line']]
    };

    function card(o) {
        var c = el('article', 'k-card k-card--' + o.status);
        c.dataset.id = o.id;
        if (fresh[o.id]) c.classList.add('is-fresh');

        var head = el('header', 'k-card-head');
        var table = el('span', 'k-table');
        table.appendChild(el('span', 'k-table-label', 'Table'));
        table.appendChild(el('strong', null, String(o.table)));
        head.appendChild(table);
        var meta = el('div', 'k-meta');
        if (o.tableName) meta.appendChild(el('span', 'k-area', o.tableName));
        meta.appendChild(el('span', 'k-num', '#' + o.number + ' · ' + clock(o.created)));
        head.appendChild(meta);

        var age = el('span', 'k-age');
        var since = o.status === 'new' || o.status === 'preparing' ? o.created : o.updated;
        var mins = minutesSince(o.created);
        var late = (o.status === 'new' && mins >= LATE_NEW) || (o.status === 'preparing' && mins >= LATE_PREPARING);
        if (late) { age.classList.add('is-late'); age.appendChild(icon('ph-warning')); age.appendChild(document.createTextNode(' Late · ')); }
        age.appendChild(document.createTextNode(o.status === 'served' ? 'Served ' + ago(since) + (ago(since) === 'just now' ? '' : ' ago')
            : o.status === 'cancelled' ? 'Cancelled' : ago(since)));
        head.appendChild(age);
        c.appendChild(head);

        var list = el('ul', 'k-items');
        o.items.forEach(function (i) {
            var li = el('li');
            li.appendChild(el('span', 'k-qty', i.qty + '×'));
            var name = el('span', 'k-dish', i.name);
            if (i.options) name.appendChild(el('small', null, i.options));
            li.appendChild(name);
            list.appendChild(li);
        });
        c.appendChild(list);

        if (o.note) {
            var note = el('p', 'k-note');
            note.appendChild(icon('ph-note-pencil'));
            note.appendChild(el('span', null, o.note));
            c.appendChild(note);
        }

        var foot = el('footer', 'k-card-foot');
        foot.appendChild(el('span', 'k-total', CURRENCY + money.format(o.total)));
        var actions = el('div', 'k-actions');
        (ACTIONS[o.status] || []).forEach(function (a) {
            var b = el('button', 'k-btn k-btn--' + a[3]);
            b.type = 'button';
            b.dataset.to = a[2];
            b.appendChild(icon(a[1]));
            b.appendChild(document.createTextNode(a[0]));
            b.setAttribute('aria-label', a[0] + ': table ' + o.table + ', order ' + o.number);
            actions.appendChild(b);
        });
        if (o.status === 'new' || o.status === 'preparing') {
            var x = el('button', 'k-btn k-btn--icon');
            x.type = 'button';
            x.dataset.to = 'cancelled';
            x.setAttribute('aria-label', 'Cancel order ' + o.number + ' (table ' + o.table + ')');
            x.title = 'Cancel order';
            x.appendChild(icon('ph-x'));
            actions.appendChild(x);
        }
        foot.appendChild(actions);
        c.appendChild(foot);
        return c;
    }

    function render() {
        var cols = { 'new': [], 'preparing': [], 'done': [] };
        Object.keys(orders).forEach(function (id) { var o = orders[id]; cols[column(o)].push(o); });
        // Open orders oldest first (cook in order); done ones newest first.
        cols['new'].sort(function (a, b) { return a.created < b.created ? -1 : 1; });
        cols.preparing.sort(function (a, b) { return a.created < b.created ? -1 : 1; });
        cols.done.sort(function (a, b) { return a.updated > b.updated ? -1 : 1; });
        cols.done = cols.done.slice(0, 30);

        Object.keys(cols).forEach(function (k) {
            var list = $('[data-list="' + k + '"]');
            list.textContent = '';
            cols[k].forEach(function (o) { list.appendChild(card(o)); });
            $('[data-empty="' + k + '"]').hidden = cols[k].length > 0;
            document.querySelectorAll('[data-count="' + k + '"]').forEach(function (n) { n.textContent = cols[k].length; });
        });
        var waiting = cols['new'].length;
        document.title = (waiting ? '(' + waiting + ') ' : '') + TITLE;
    }

    /* ---------- Data ---------- */
    function upsert(o, isPush) {
        var before = orders[o.id];
        if (before && before.updated > o.updated) return; // an older message arrived late
        if (isPush && !before && o.status === 'new' && loadedOnce) {
            fresh[o.id] = true;
            setTimeout(function () { delete fresh[o.id]; render(); }, 15000);
            chime();
        }
        orders[o.id] = o;
    }

    function load() {
        return fetch(ORDERS_URL, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (r.status === 404 || r.status === 401 || r.status === 403 || r.redirected) { setDenied(); throw new Error('denied'); }
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.json();
            })
            .then(function (data) {
                serverOffset = new Date(data.now).getTime() - Date.now();
                var known = orders;
                orders = {};
                data.orders.forEach(function (o) {
                    // Orders that arrived while the screen was reconnecting still get the highlight.
                    if (loadedOnce && !known[o.id] && o.status === 'new') { fresh[o.id] = true; chime(); setTimeout(function () { delete fresh[o.id]; render(); }, 15000); }
                    orders[o.id] = o;
                });
                loadedOnce = true;
                setPaused(data.paused, data.enabled);
                render();
            });
    }

    function setDenied() {
        denied = true;
        $('[data-denied-banner]').hidden = false;
        orders = {};
        render();
        if (connection) connection.stop();
    }

    function post(url, data) {
        var form = new URLSearchParams(data);
        form.set('__RequestVerificationToken', TOKEN);
        return fetch(url, {
            method: 'POST', credentials: 'same-origin', body: form,
            headers: { 'Accept': 'application/json', 'RequestVerificationToken': TOKEN }
        });
    }

    /* ---------- Buttons on cards ---------- */
    document.querySelector('.k-board').addEventListener('click', function (e) {
        var b = e.target.closest('button[data-to]');
        if (!b) return;
        var c = b.closest('.k-card');
        var o = orders[c.dataset.id];
        if (!o) return;
        var to = b.dataset.to;
        if (to === 'cancelled' && !window.confirm('Cancel order #' + o.number + ' for table ' + o.table + '?')) return;
        c.querySelectorAll('button').forEach(function (x) { x.disabled = true; });
        c.classList.add('is-busy');
        post(STATUS_URL + o.id + '/status', { from: o.status, to: to })
            .then(function (r) {
                if (r.status === 404) { delete orders[o.id]; render(); return; }
                if (!r.ok && r.status !== 409) throw new Error('HTTP ' + r.status);
                return r.json().then(function (data) {
                    if (r.status === 409) toast('Another screen already moved order #' + data.order.number + '.');
                    delete fresh[data.order.id];
                    orders[data.order.id] = data.order;
                    render();
                });
            })
            .catch(function () {
                toast("Couldn't save. Check the connection and try again.");
                render();
            });
    });

    /* ---------- Pause ---------- */
    var pauseBtn = $('[data-pause]');
    function setPaused(paused, enabled) {
        pauseBtn.hidden = !enabled;
        pauseBtn.setAttribute('aria-pressed', paused ? 'true' : 'false');
        pauseBtn.querySelector('i').className = 'ph ' + (paused ? 'ph-play' : 'ph-pause');
        pauseBtn.querySelector('span').textContent = paused ? 'Resume orders' : 'Pause orders';
        $('[data-paused-banner]').hidden = !(paused && enabled);
        $('[data-off-banner]').hidden = enabled;
    }
    pauseBtn.addEventListener('click', function () {
        var paused = pauseBtn.getAttribute('aria-pressed') !== 'true';
        if (paused && !window.confirm('Pause new orders? Guests will be asked to order with their server until you resume.')) return;
        pauseBtn.disabled = true;
        post(PAUSE_URL, { paused: paused })
            .then(function (r) { if (!r.ok) throw new Error(); setPaused(paused, true); toast(paused ? 'New orders paused.' : 'Orders resumed.'); })
            .catch(function () { toast("Couldn't save. Check the connection and try again."); })
            .then(function () { pauseBtn.disabled = false; });
    });

    /* ---------- Sound: a short two-note chime made with Web Audio ---------- */
    var SOUND_KEY = 'kitchen_sound';
    var soundOn = false, audio = null;
    try { soundOn = localStorage.getItem(SOUND_KEY) === 'on'; } catch (e) { /* private mode */ }
    var soundBtn = $('[data-sound]');
    function ensureAudio() {
        if (!audio) { try { audio = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { audio = null; } }
        if (audio && audio.state === 'suspended') audio.resume();
        return audio;
    }
    function syncSound() {
        soundBtn.setAttribute('aria-pressed', soundOn ? 'true' : 'false');
        soundBtn.querySelector('i').className = 'ph ' + (soundOn ? 'ph-speaker-high' : 'ph-speaker-slash');
        soundBtn.querySelector('span').textContent = soundOn ? 'Sound on' : 'Sound off';
        $('[data-sound-banner]').hidden = !(soundOn && (!audio || audio.state !== 'running'));
    }
    function chime() {
        if (!soundOn) return;
        var ctx = ensureAudio();
        if (!ctx || ctx.state !== 'running') return;
        [[880, 0], [1320, 0.18]].forEach(function (n) {
            var osc = ctx.createOscillator(), gain = ctx.createGain();
            osc.type = 'sine';
            osc.frequency.value = n[0];
            var t = ctx.currentTime + n[1];
            gain.gain.setValueAtTime(0.0001, t);
            gain.gain.exponentialRampToValueAtTime(0.35, t + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.5);
            osc.connect(gain).connect(ctx.destination);
            osc.start(t);
            osc.stop(t + 0.55);
        });
    }
    soundBtn.addEventListener('click', function () {
        soundOn = !soundOn;
        try { localStorage.setItem(SOUND_KEY, soundOn ? 'on' : 'off'); } catch (e) { /* not fatal */ }
        if (soundOn) { ensureAudio(); setTimeout(function () { syncSound(); chime(); }, 50); }
        syncSound();
    });
    // Browsers only start audio after a touch; the first one anywhere unlocks it.
    document.addEventListener('pointerdown', function () {
        if (soundOn) { ensureAudio(); setTimeout(syncSound, 50); }
    });
    syncSound();

    /* ---------- Full screen + keep the screen awake ---------- */
    var fsBtn = $('[data-fullscreen]');
    if (!document.documentElement.requestFullscreen) fsBtn.hidden = true;
    fsBtn.addEventListener('click', function () {
        if (document.fullscreenElement) document.exitFullscreen();
        else document.documentElement.requestFullscreen().catch(function () { /* refused */ });
    });
    var wakeLock = null;
    function keepAwake() {
        if (!('wakeLock' in navigator) || document.visibilityState !== 'visible') return;
        navigator.wakeLock.request('screen').then(function (l) { wakeLock = l; }).catch(function () { /* not allowed */ });
    }
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') { keepAwake(); if (!denied) load().catch(function () {}); }
    });
    keepAwake();

    /* ---------- Phones: one column at a time ---------- */
    document.querySelectorAll('[data-tab]').forEach(function (t) {
        t.addEventListener('click', function () {
            document.querySelectorAll('[data-tab]').forEach(function (x) { x.setAttribute('aria-pressed', x === t ? 'true' : 'false'); });
            $('.k-board').dataset.show = t.dataset.tab;
        });
    });

    /* ---------- Live connection ---------- */
    var conn = $('[data-conn]');
    function setConn(state, text) { conn.dataset.conn = state; conn.querySelector('[data-conn-text]').textContent = text; }

    var connection = null;
    if (window.signalR) {
        connection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL)
            .withAutomaticReconnect({ nextRetryDelayInMilliseconds: function (ctx) { return [0, 2000, 5000, 10000][ctx.previousRetryCount] || 15000; } })
            .build();
        connection.on('order', function (o) { upsert(o, true); render(); });
        connection.on('paused', function (p) { setPaused(p, true); });
        connection.onreconnecting(function () { setConn('reconnecting', 'Reconnecting…'); });
        connection.onreconnected(function () { join(); });
        connection.onclose(function () {
            if (denied) return;
            setConn('offline', 'Offline · retrying');
            setTimeout(start, 5000);
        });
    }
    function join() {
        return connection.invoke('Join', BRANCH).then(function (ok) {
            if (!ok) { setDenied(); return; }
            setConn('live', 'Live');
            return load();
        });
    }
    function start() {
        if (!connection || denied || connection.state !== signalR.HubConnectionState.Disconnected) return;
        setConn('connecting', 'Connecting…');
        connection.start().then(join).catch(function () {
            setConn('offline', 'Offline · retrying');
            setTimeout(start, 5000);
        });
    }

    // The browser knows at once when the network drops; SignalR alone would take up to
    // 30 seconds to notice a silent drop. Say so immediately, and catch up when it's back.
    window.addEventListener('offline', function () { if (!denied) setConn('offline', 'Offline · waiting for the network'); });
    window.addEventListener('online', function () {
        if (denied || !connection) return;
        if (connection.state === signalR.HubConnectionState.Connected) { setConn('live', 'Live'); load().catch(function () {}); }
        else if (connection.state === signalR.HubConnectionState.Disconnected) start();
        else setConn('reconnecting', 'Reconnecting…');
    });

    load().catch(function () { /* shown by the connection state; retried below */ });
    if (connection) start(); else setConn('offline', 'Live updates unavailable · refreshing every 30 s');

    // Safety net and timers: reload the list and refresh "4 min" labels.
    setInterval(function () { if (!denied && document.visibilityState === 'visible') load().catch(function () {}); }, 30000);
    setInterval(render, 30000);
})();
