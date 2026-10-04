/* Booking page: times follow the party size and day without reloading, and booking
   happens in place. Without JavaScript the page is a plain form and still works.
   Names and messages come from the server; text is set with textContent. */
(function () {
    'use strict';

    // Forms that ask first (cancel booking).
    document.querySelectorAll('form[data-confirm]').forEach(function (f) {
        f.addEventListener('submit', function (e) { if (!window.confirm(f.getAttribute('data-confirm'))) e.preventDefault(); });
    });

    var cfgEl = document.getElementById('bookConfig');
    var form = document.querySelector('[data-book-form]');
    var filter = document.querySelector('[data-book-filter]');
    if (!cfgEl || !form || !filter) return;
    var C = JSON.parse(cfgEl.textContent);
    var W = C.words;

    var guests = filter.querySelector('[data-guests]');
    var mirror = form.querySelector('[data-guests-mirror]');
    var dateInput = filter.querySelector('[data-date-input]');
    var slotsBox = form.querySelector('[data-slots]');
    var emptyMsg = form.querySelector('[data-slots-empty]');
    var dayLabel = form.querySelector('[data-day-label]');
    var submit = form.querySelector('[data-book-submit]');
    var submitLabel = form.querySelector('[data-book-submit-label]');
    var requestIdInput = form.querySelector('[data-request-id]');
    var current = dateInput.value;
    var loadSeq = 0;

    function longDate(iso) {
        if (iso === C.today) return W.Today;
        var t = new Date(C.today + 'T12:00:00'); t.setDate(t.getDate() + 1);
        if (iso === t.toISOString().slice(0, 10)) return W.Tomorrow;
        try { return new Date(iso + 'T12:00:00').toLocaleDateString(C.lang, { weekday: 'long', day: 'numeric', month: 'long' }); }
        catch (e) { return iso; }
    }

    function syncUrl() {
        var p = new URLSearchParams(location.search);
        p.set('date', current); p.set('guests', guests.value);
        history.replaceState(null, '', '?' + p.toString());
    }

    function showError(field, text) {
        form.querySelectorAll('[data-error]').forEach(function (e) {
            var on = e.getAttribute('data-error') === field && !!text;
            e.hidden = !on;
            e.textContent = on ? text : '';
            var input = form.querySelector('[name="' + ({ name: 'Name', phone: 'Phone', email: 'Email' }[e.getAttribute('data-error')] || '') + '"]');
            if (input) { if (on) input.setAttribute('aria-invalid', 'true'); else input.removeAttribute('aria-invalid'); }
        });
    }

    function renderSlots(data, keep) {
        slotsBox.textContent = '';
        var any = false;
        data.slots.forEach(function (s) {
            var label = document.createElement('label');
            label.className = 'b-slot';
            var input = document.createElement('input');
            input.type = 'radio'; input.name = 'Time'; input.required = true;
            input.value = s.date + 'T' + s.time;
            input.disabled = !s.open;
            if (s.open && input.value === keep) input.checked = true;
            var span = document.createElement('span');
            span.textContent = s.time;
            label.appendChild(input); label.appendChild(span);
            slotsBox.appendChild(label);
            if (s.open) any = true;
        });
        emptyMsg.hidden = any;
        emptyMsg.textContent = data.closed ? W.ClosedDay : W.NoSlots;
    }

    function load() {
        var keep = (form.querySelector('input[name="Time"]:checked') || {}).value;
        var seq = ++loadSeq;
        mirror.value = guests.value;
        dayLabel.textContent = longDate(current);
        slotsBox.setAttribute('aria-busy', 'true');
        emptyMsg.hidden = false; emptyMsg.textContent = W.Loading;
        slotsBox.textContent = '';
        syncUrl();
        fetch(C.slotsUrl + '?date=' + encodeURIComponent(current) + '&guests=' + encodeURIComponent(guests.value), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (data) { if (seq === loadSeq) renderSlots(data, keep); })
            .catch(function () { if (seq === loadSeq) { emptyMsg.hidden = false; emptyMsg.textContent = W.Network; } })
            .then(function () { slotsBox.removeAttribute('aria-busy'); });
    }

    function stepperState() {
        var v = Number(guests.value), max = guests.options.length;
        filter.querySelector('[data-step="-1"]').disabled = v <= 1;
        filter.querySelector('[data-step="1"]').disabled = v >= max;
    }
    filter.querySelectorAll('[data-step]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var v = Math.min(guests.options.length, Math.max(1, Number(guests.value) + Number(btn.getAttribute('data-step'))));
            guests.value = String(v); stepperState(); load();
        });
    });
    guests.addEventListener('change', function () { stepperState(); load(); });

    function selectDate(iso) {
        current = iso;
        dateInput.value = iso;
        filter.querySelectorAll('[data-date]').forEach(function (a) {
            if (a.getAttribute('data-date') === iso) a.setAttribute('aria-current', 'date'); else a.removeAttribute('aria-current');
        });
        load();
    }
    filter.querySelectorAll('[data-date]').forEach(function (a) {
        a.addEventListener('click', function (e) { e.preventDefault(); selectDate(a.getAttribute('data-date')); });
    });
    dateInput.addEventListener('change', function () { if (dateInput.value) selectDate(dateInput.value); });
    filter.addEventListener('submit', function (e) { e.preventDefault(); load(); });

    /* ---------- Booking ---------- */
    function uuid() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = crypto.getRandomValues(new Uint8Array(1))[0] % 16; return (c === 'x' ? r : (r & 3) | 8).toString(16);
        });
    }
    // A new attempt id whenever the form changes; the same one when the same booking is retried.
    form.addEventListener('input', function () { requestIdInput.value = ''; });
    form.addEventListener('change', function () { requestIdInput.value = ''; });

    var busy = false;
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        if (busy) return;
        showError(null);
        if (!form.querySelector('input[name="Time"]:checked')) { showError('time', emptyMsg.hidden ? W.NoSlots : emptyMsg.textContent); slotsBox.scrollIntoView({ block: 'center', behavior: 'smooth' }); return; }
        if (!requestIdInput.value) requestIdInput.value = uuid();
        busy = true; submit.disabled = true; submitLabel.textContent = W.Booking;
        fetch(form.action, { method: 'POST', body: new URLSearchParams(new FormData(form)), headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (r.status === 429) throw new Error('busy');
                return r.json().then(function (data) {
                    if (data.ok) { location.href = data.url; return; }
                    showError(data.field, data.message);
                    if (data.field === 'time') load(); // someone took it: show what's left
                    var bad = form.querySelector('[aria-invalid="true"]');
                    if (bad) bad.focus();
                    busy = false; submit.disabled = false; submitLabel.textContent = W.Book;
                });
            })
            .catch(function () {
                showError('time', W.Network); // same request id on retry: never books twice
                busy = false; submit.disabled = false; submitLabel.textContent = W.Book;
            });
    });
})();
