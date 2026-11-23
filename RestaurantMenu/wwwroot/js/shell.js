/* Back office frame (docs/PLAN-backoffice.md, phase 1): collapsed side panel ("rail"), remembered
   open sections, and the Ctrl/⌘ K command palette. Everything works without it; this only adds
   comfort. Preferences live in this browser only (localStorage), so every access is guarded. */
(function () {
    'use strict';

    var RAIL_KEY = 'mqm.rail';
    var OPEN_KEY = 'mqm.navOpen';

    function load(key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    }
    function save(key, value) {
        try { window.localStorage.setItem(key, value); } catch (e) { /* private mode: not remembered */ }
    }
    function openKeys() {
        try { return JSON.parse(load(OPEN_KEY) || '[]'); } catch (e) { return []; }
    }

    var app = document.querySelector('.app');
    var groups = Array.prototype.slice.call(document.querySelectorAll('[data-nav-group]'));

    /* ---------- Sections: reopen the ones the person left open ---------- */
    var remembered = openKeys();
    groups.forEach(function (g) {
        if (remembered.indexOf(g.dataset.navGroup) >= 0) g.open = true;
    });
    function rememberOpen() {
        if (app && app.classList.contains('app--rail')) return; // the rail opens everything; don't store that
        save(OPEN_KEY, JSON.stringify(groups.filter(function (g) { return g.open; }).map(function (g) { return g.dataset.navGroup; })));
    }
    groups.forEach(function (g) { g.addEventListener('toggle', rememberOpen); });

    /* ---------- Rail: icons only, every page one click away ---------- */
    var railToggle = document.querySelector('[data-rail-toggle]');
    function setRail(on) {
        if (!app) return;
        app.classList.toggle('app--rail', on);
        if (railToggle) {
            railToggle.setAttribute('aria-pressed', on ? 'true' : 'false');
            var label = on ? 'Expand the side panel' : 'Collapse the side panel';
            railToggle.setAttribute('aria-label', label);
            railToggle.setAttribute('title', label);
        }
        if (on) {
            groups.forEach(function (g) { g.open = true; });
        } else {
            var keep = openKeys();
            groups.forEach(function (g) {
                g.open = keep.indexOf(g.dataset.navGroup) >= 0 || !!g.querySelector('[aria-current="page"]');
            });
        }
    }
    if (load(RAIL_KEY) === '1') setRail(true);
    if (railToggle) {
        railToggle.addEventListener('click', function () {
            var on = !app.classList.contains('app--rail');
            save(RAIL_KEY, on ? '1' : '0');
            setRail(on);
        });
    }

    /* ---------- Command palette ---------- */
    var dialog = document.getElementById('cmdk');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    var input = dialog.querySelector('[data-cmdk-input]');
    var items = Array.prototype.slice.call(dialog.querySelectorAll('[data-cmdk-item]'));
    var empty = dialog.querySelector('[data-cmdk-empty]');
    var isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
    document.querySelectorAll('[data-cmdk-hint]').forEach(function (k) { k.textContent = isMac ? '⌘K' : 'Ctrl K'; });

    // Letters without accents, so "cilesimet" finds "cilësimet".
    function fold(s) {
        return (s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    }
    items.forEach(function (li, i) {
        li.dataset.cmdkFold = fold(li.dataset.cmdkText);
        li.id = 'cmdk-opt-' + i;
    });

    var visible = items;
    var active = 0;

    function select(index) {
        visible.forEach(function (li, i) { li.setAttribute('aria-selected', i === index ? 'true' : 'false'); });
        active = index;
        var current = visible[index];
        if (current) {
            current.scrollIntoView({ block: 'nearest' });
            input.setAttribute('aria-activedescendant', current.id);
        } else {
            input.removeAttribute('aria-activedescendant');
        }
    }

    function filter() {
        var words = fold(input.value).split(/\s+/).filter(Boolean);
        visible = items.filter(function (li) {
            var match = words.every(function (w) { return li.dataset.cmdkFold.indexOf(w) >= 0; });
            li.hidden = !match;
            return match;
        });
        if (empty) empty.hidden = visible.length > 0;
        select(visible.length ? 0 : -1);
    }

    function open() {
        if (dialog.open) return;
        input.value = '';
        filter();
        dialog.showModal();
        input.focus();
    }

    function go(li) {
        var link = li && li.querySelector('a');
        if (link) window.location.assign(link.href);
    }

    document.querySelectorAll('[data-cmdk-open]').forEach(function (b) { b.addEventListener('click', open); });

    document.addEventListener('keydown', function (e) {
        var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName) || e.target.isContentEditable;
        if ((e.key === 'k' || e.key === 'K') && (e.metaKey || e.ctrlKey)) {
            e.preventDefault();
            open();
        } else if (e.key === '/' && !typing && !dialog.open) {
            e.preventDefault();
            open();
        }
    });

    input.addEventListener('input', filter);
    input.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            if (visible.length) select((active + 1) % visible.length);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            if (visible.length) select((active - 1 + visible.length) % visible.length);
        } else if (e.key === 'Enter') {
            e.preventDefault();
            go(visible[active]);
        }
    });

    items.forEach(function (li) {
        li.addEventListener('mousemove', function () {
            var i = visible.indexOf(li);
            if (i >= 0 && i !== active) select(i);
        });
    });

    // A click on the dimmed backdrop closes the palette.
    dialog.addEventListener('click', function (e) {
        if (e.target === dialog) dialog.close();
    });
})();
