/* The floor plan on Areas & layout (Views/Tables/Index): drag tables on the grid, or onto another area,
   or select one and move it with the arrow keys. Each move shows at once and is saved to
   /branch/{id}/tables/{table}/position, which snaps it to the grid; if saving fails the table goes back.
   Positions are 0–100 % of the area, drawn by floor.css with a margin (7 % + x × 0.86, 9 % + y × 0.82),
   so the editor and the live floor show tables in the same places. */
(function () {
    'use strict';

    var root = document.querySelector('.floor-editor');
    if (!root || !window.MqmDrag) return;

    var BASE = root.dataset.positionUrl;
    var TOKEN = (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    var STEP = 2.5, BIG_STEP = 10, SAVE_DELAY_MS = 450;
    var phone = window.matchMedia('(max-width: 767.98px)');
    var status = root.querySelector('[data-plan-status]');
    var timers = {};

    function snap(v) { return Math.round(Math.min(100, Math.max(0, v)) / STEP) * STEP; }

    function place(table, x, y) {
        table.dataset.x = String(x);
        table.dataset.y = String(y);
        table.style.setProperty('--table-x', x + '%');
        table.style.setProperty('--table-y', y + '%');
    }

    function areaName(canvas) { return canvas.getAttribute('aria-label') || 'this area'; }

    function recount() {
        root.querySelectorAll('.floor-editor-area').forEach(function (area) {
            var n = area.querySelectorAll('[data-table]').length;
            area.querySelector('[data-area-count]').textContent = n;
        });
    }

    function announce(text) { if (status) status.textContent = text; }

    function flash(text) {
        if (window.mqmToast) window.mqmToast(text); // site.js
        announce(text);
    }

    /** Saves a table's spot; on failure puts it back where it was. */
    function save(table, before) {
        var form = new URLSearchParams();
        form.set('x', table.dataset.x);
        form.set('y', table.dataset.y);
        form.set('areaId', table.closest('[data-drop]').dataset.drop);
        form.set('__RequestVerificationToken', TOKEN);
        table.classList.add('is-saving');
        return fetch(BASE + table.dataset.table + '/position', {
            method: 'POST', credentials: 'same-origin', body: form,
            headers: { 'Accept': 'application/json', 'RequestVerificationToken': TOKEN }
        }).then(function (r) {
            return r.json().catch(function () { return {}; }).then(function (data) {
                if (!r.ok) throw new Error(data.message || "Couldn't save the table. Check the connection and try again.");
                place(table, data.x, data.y);
            });
        }).catch(function (err) {
            if (before) {
                before.parent.appendChild(table);
                place(table, before.x, before.y);
                recount();
            }
            flash(err.message);
        }).then(function () { table.classList.remove('is-saving'); });
    }

    function snapshot(table) {
        return { parent: table.parentNode, x: Number(table.dataset.x), y: Number(table.dataset.y) };
    }

    MqmDrag.attach(root, {
        handle: '[data-table]',
        zones: '.floor-editor [data-drop]',
        start: function () { return !phone.matches; },
        drop: function (table, canvas, point, offset) {
            if (!canvas) return;
            var before = snapshot(table);
            var rect = canvas.getBoundingClientRect();
            var size = table.getBoundingClientRect();
            var cx = point.x - offset.x + size.width / 2;
            var cy = point.y - offset.y + size.height / 2;
            var x = snap(((cx - rect.left) / rect.width * 100 - 7) / 0.86);
            var y = snap(((cy - rect.top) / rect.height * 100 - 9) / 0.82);
            if (canvas !== table.parentNode) canvas.appendChild(table);
            place(table, x, y);
            recount();
            table.focus({ preventScroll: true });
            announce('Table ' + table.querySelector('strong').textContent + ' moved in ' + areaName(canvas) + '.');
            save(table, before);
        }
    });

    // Keyboard: arrows move the focused table one grid step (Shift: four), saved once the keys stop.
    root.addEventListener('keydown', function (e) {
        var table = e.target.closest('[data-table]');
        if (!table) return;
        var d = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
        if (!d) return;
        e.preventDefault();
        var step = e.shiftKey ? BIG_STEP : STEP;
        var id = table.dataset.table;
        if (!timers[id]) timers[id] = { before: snapshot(table) };
        place(table, snap(Number(table.dataset.x) + d[0] * step), snap(Number(table.dataset.y) + d[1] * step));
        clearTimeout(timers[id].t);
        timers[id].t = setTimeout(function () {
            var before = timers[id].before;
            delete timers[id];
            announce('Table ' + table.querySelector('strong').textContent + ' moved.');
            save(table, before);
        }, SAVE_DELAY_MS);
    });
})();
