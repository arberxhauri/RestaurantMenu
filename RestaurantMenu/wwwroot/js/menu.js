/* Public menu: category scroll-spy, search, dish sheet, and the guest's saved list.
   The list lives only in this guest's browser (localStorage). At a table set up for
   ordering (QR code with ?t= and k=), the list can be sent to the kitchen, and the
   phone follows its orders' status. */
(function () {
    'use strict';

    var body = document.body;
    var KEY = body.dataset.storageKey || 'menu_fav';
    var CURRENCY = body.dataset.currency || '';
    var L = {
        save: body.dataset.labelSave,
        saved: body.dataset.labelSaved,
        empty: body.dataset.labelEmpty,
        emptyHint: body.dataset.labelEmptyHint,
        soldOut: body.dataset.labelSoldout,
        required: body.dataset.labelRequired || 'Required',
        optional: body.dataset.labelOptional || 'Optional',
        upTo: body.dataset.labelUpTo || 'up to {0}',
        pleaseChoose: body.dataset.labelPleaseChoose || 'Please choose: {0}'
    };
    // Every sold-out dish, including ones the restaurant hides, so a guest's saved list can flag them.
    var SOLD_OUT = {};
    (body.dataset.soldoutIds || '').split(',').forEach(function (id) { if (id) SOLD_OUT[id] = true; });
    function isSoldOut(id) { return SOLD_OUT[String(id)] === true; }

    /* ---------- Anonymous usage counts for the restaurant ----------
       Which dishes get opened or saved, and language switches. Only the menu, the
       event, the dish and the language are sent: no cookie, no id, nothing about
       the guest. sendBeacon also delivers while the page is navigating away. */
    var EVENT_URL = body.dataset.eventUrl;
    var BRANCH_ID = body.dataset.branchId;
    function track(type, dishId, lang) {
        if (!EVENT_URL || !BRANCH_ID || !navigator.sendBeacon) return;
        var data = new URLSearchParams({ b: BRANCH_ID, t: type });
        if (dishId) data.set('p', dishId);
        if (lang) data.set('l', lang);
        try { navigator.sendBeacon(EVENT_URL, data); } catch (e) { /* never break the menu */ }
    }

    var money = new Intl.NumberFormat(document.documentElement.lang || undefined, {
        minimumFractionDigits: 2, maximumFractionDigits: 2
    });
    function fmt(n) { return CURRENCY + money.format(n); }

    /* ---------- Storage (wrapped: private mode can throw) ---------- */
    var list = [];
    function load() {
        try {
            var raw = localStorage.getItem(KEY);
            var parsed = raw ? JSON.parse(raw) : [];
            // key = dish id, plus the chosen options for dishes with choices. Lists saved
            // before options existed have no key: their dish id is used.
            list = Array.isArray(parsed) ? parsed.map(function (i) {
                return {
                    key: String(i.key || i.id), id: String(i.id), name: String(i.name),
                    price: Number(i.price) || 0, qty: Math.max(1, Number(i.qty) || 1),
                    optText: i.optText ? String(i.optText) : ''
                };
            }) : [];
        } catch (e) { list = []; }
    }
    function persist() {
        try { localStorage.setItem(KEY, JSON.stringify(list)); } catch (e) { /* not fatal */ }
        requestId = null; // a changed list is a new order
    }

    /* ---------- Table ordering: config and this phone's orders ---------- */
    var ORDER = {};
    try { ORDER = JSON.parse((document.getElementById('orderConfig') || {}).textContent || '{}') || {}; } catch (e) { ORDER = {}; }
    var OW = ORDER.words || {};
    var ORDERS_KEY = 'menu_orders_' + (ORDER.branch || BRANCH_ID);
    var ORDER_TTL = 12 * 3600 * 1000; // a phone shows its orders for 12 hours
    var requestId = null;              // kept across retries of the same list, so a retry never orders twice
    var myOrders = [];
    function loadOrders() {
        try {
            var raw = JSON.parse(localStorage.getItem(ORDERS_KEY) || '[]');
            var cutoff = Date.now() - ORDER_TTL;
            myOrders = Array.isArray(raw) ? raw.filter(function (o) { return o && o.id && o.t > cutoff; }) : [];
        } catch (e) { myOrders = []; }
    }
    function saveOrders() {
        try { localStorage.setItem(ORDERS_KEY, JSON.stringify(myOrders.slice(0, 10))); } catch (e) { /* not fatal */ }
    }
    function isFinal(o) { return o.status === 'served' || o.status === 'cancelled'; }
    loadOrders();
    function find(key) { return list.find(function (i) { return i.key === key; }); }
    function listed(id) { return list.some(function (i) { return i.id === id; }); }

    var items = Array.prototype.slice.call(document.querySelectorAll('[data-item]'));
    function itemData(li) {
        return {
            id: li.dataset.id,
            name: li.dataset.name,
            desc: li.dataset.desc,
            nutrition: li.dataset.nutrition,
            price: parseFloat(li.dataset.price) || 0,
            priceLabel: li.dataset.priceLabel,
            img: li.dataset.img,
            soldOut: li.dataset.soldout === 'true',
            later: li.dataset.later === 'true',
            badge: li.dataset.badge,
            allergenText: li.dataset.allergenText,
            options: li.dataset.options ? JSON.parse(li.dataset.options) : null,
            tags: li.querySelector('[data-tags]')
        };
    }

    // Dishes without choices: the + button adds or removes the dish.
    function toggle(data) {
        var existing = find(data.id);
        if (existing) list = list.filter(function (i) { return i !== existing; });
        else { list.push({ key: data.id, id: data.id, name: data.name, price: data.price, qty: 1, optText: '' }); track('add', data.id); }
        persist();
        render();
    }

    // Dishes with choices: each combination is its own line; the same one again adds 1.
    function addWithOptions(data, picks) {
        var ids = picks.map(function (c) { return c.o.id; }).sort(function (a, b) { return a - b; });
        var key = data.id + ':' + ids.join('-');
        var existing = find(key);
        if (existing) existing.qty += 1;
        else list.push({
            key: key, id: data.id, name: data.name,
            price: data.price + picks.reduce(function (s, c) { return s + c.o.delta; }, 0),
            qty: 1,
            optText: picks.map(function (c) { return c.o.name; }).join(', ')
        });
        track('add', data.id);
        persist();
        render();
    }

    /* ---------- Rendering ---------- */
    var fab = document.querySelector('[data-fab]');
    var fabCount = document.querySelector('[data-fab-count]');
    var fabTotal = document.querySelector('[data-fab-total]');
    var fabLabel = document.querySelector('[data-fab-label]');
    var fabLabelText = fabLabel ? fabLabel.textContent : '';
    var sendBtn = document.querySelector('[data-order-send]');
    var sending = false;

    function renderOrders() {
        var box = document.querySelector('[data-orders]');
        if (!box) return;
        var ul = box.querySelector('[data-orders-list]');
        ul.textContent = '';
        box.hidden = myOrders.length === 0;
        myOrders.slice(0, 5).forEach(function (o) {
            var li = document.createElement('li');
            var name = document.createElement('strong');
            name.textContent = (OW.orderNumber || '#{0}').replace('{0}', o.n);
            var meta = document.createElement('small');
            meta.textContent = o.count + ' ' + (o.count === 1 ? OW.item : OW.items) + ' · ' + fmt(o.total);
            var st = document.createElement('span');
            st.className = 'm-order-status m-order-status--' + o.status;
            st.textContent = o.statusText;
            var text = document.createElement('div');
            text.appendChild(name);
            text.appendChild(meta);
            li.appendChild(text);
            li.appendChild(st);
            ul.appendChild(li);
        });
    }
    var listEl = document.querySelector('[data-list]');
    var listFoot = document.querySelector('[data-list-foot]');
    var listTotal = document.querySelector('[data-list-total]');

    // Sold-out dishes stay on the list (flagged) but can't be ordered, so they don't count.
    function total() { return list.reduce(function (s, i) { return isSoldOut(i.id) ? s : s + i.price * i.qty; }, 0); }
    function count() { return list.reduce(function (s, i) { return s + i.qty; }, 0); }

    function render() {
        items.forEach(function (li) {
            var btn = li.querySelector('[data-save]');
            if (!btn) return; // sold out: no + button
            var on = listed(li.dataset.id);
            btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            btn.setAttribute('aria-label', (on ? L.saved : L.save) + ': ' + li.dataset.name);
            btn.innerHTML = on ? '<i class="ph-fill ph-check" aria-hidden="true"></i>' : '<i class="ph ph-plus" aria-hidden="true"></i>';
        });

        if (fab) {
            // With an empty list, the button follows the newest order instead ("#12 · Being prepared").
            var latest = list.length === 0 ? myOrders[0] : null;
            fab.hidden = list.length === 0 && !latest;
            fabCount.textContent = latest ? '#' + latest.n : count();
            fabLabel.textContent = latest ? latest.statusText : fabLabelText;
            fabTotal.textContent = latest ? '' : fmt(total());
        }
        renderOrders();
        if (sendBtn) sendBtn.disabled = sending || list.length === 0;

        if (!listEl) return;
        listEl.textContent = '';
        if (!list.length) {
            var empty = document.createElement('li');
            empty.className = 'm-list-empty';
            empty.textContent = L.empty + '. ' + L.emptyHint + '.';
            listEl.appendChild(empty);
            listFoot.hidden = true;
            return;
        }
        listFoot.hidden = false;
        list.forEach(function (i) {
            var li = document.createElement('li');
            var soldOut = isSoldOut(i.id);
            if (soldOut) li.className = 'is-soldout';

            var name = document.createElement('div');
            name.className = 'm-list-name';
            name.textContent = i.name;
            if (i.optText) {
                var opts = document.createElement('small');
                opts.className = 'm-list-opts';
                opts.textContent = i.optText;
                name.appendChild(opts);
            }
            var unit = document.createElement('small');
            unit.textContent = fmt(i.price);
            name.appendChild(unit);
            if (soldOut) {
                var flag = document.createElement('span');
                flag.className = 'm-soldout';
                flag.textContent = L.soldOut;
                name.appendChild(flag);
            }

            var qty = document.createElement('div');
            qty.className = 'm-qty';
            qty.innerHTML =
                '<button type="button" data-dec aria-label="Remove one"><i class="ph ph-minus" aria-hidden="true"></i></button>' +
                '<span class="tabular"></span>' +
                '<button type="button" data-inc aria-label="Add one"><i class="ph ph-plus" aria-hidden="true"></i></button>';
            qty.querySelector('span').textContent = i.qty;
            if (soldOut) qty.querySelector('[data-inc]').disabled = true;
            qty.querySelector('[data-dec]').addEventListener('click', function () {
                i.qty -= 1;
                if (i.qty < 1) list = list.filter(function (x) { return x !== i; });
                persist(); render();
            });
            qty.querySelector('[data-inc]').addEventListener('click', function () {
                i.qty += 1; persist(); render();
            });

            li.appendChild(name);
            li.appendChild(qty);
            listEl.appendChild(li);
        });
        listTotal.textContent = fmt(total());
    }

    /* ---------- Save buttons on each dish ---------- */
    items.forEach(function (li) {
        var btn = li.querySelector('[data-save]');
        if (btn) btn.addEventListener('click', function () {
            // Razor renders null data-* attributes as empty strings, so compare the value.
            if (btn.getAttribute('data-choose') === 'true') { openDish(li); return; } // needs choices first
            toggle(itemData(li));
            btn.classList.remove('is-pop');
            void btn.offsetWidth; // restart the feedback animation
            btn.classList.add('is-pop');
        });
        li.querySelector('[data-open]').addEventListener('click', function () { openDish(li); });
    });

    /* ---------- Sheets (native <dialog>) ---------- */
    function openSheet(d) {
        if (typeof d.showModal === 'function') d.showModal();
        else d.setAttribute('open', '');
    }
    function closeSheet(d) {
        if (typeof d.close === 'function') d.close();
        else d.removeAttribute('open');
    }
    document.querySelectorAll('dialog').forEach(function (d) {
        d.addEventListener('click', function (e) {
            if (e.target === d || e.target.closest('[data-close]')) closeSheet(d);
        });
    });

    var dish = document.querySelector('[data-dish-sheet]');
    var dishImg = dish.querySelector('[data-dish-img]');
    var dishSave = dish.querySelector('[data-dish-save]');
    var dishSoldOut = dish.querySelector('[data-dish-soldout]');
    var optWrap = dish.querySelector('[data-dish-options]');
    var optError = dish.querySelector('[data-dish-options-error]');
    var dishPrice = dish.querySelector('[data-dish-price]');
    var current = null;

    /* Option groups in the dish sheet. Built with textContent: names come from the restaurant. */
    function groupBox(g) { return optWrap.querySelector('[data-group="' + g.id + '"]'); }
    function picked() {
        var out = [];
        (current.options || []).forEach(function (g) {
            var box = groupBox(g);
            g.options.forEach(function (o) {
                var input = box.querySelector('input[value="' + o.id + '"]');
                if (input && input.checked) out.push({ g: g, o: o });
            });
        });
        return out;
    }
    function syncOptions() {
        var locked = current.soldOut || current.later;
        (current.options || []).forEach(function (g) {
            if (g.max <= 1) return;
            var inputs = groupBox(g).querySelectorAll('input');
            var n = Array.prototype.filter.call(inputs, function (i) { return i.checked; }).length;
            inputs.forEach(function (i) { if (!i.checked) i.disabled = locked || n >= g.max; });
        });
        var price = current.price + picked().reduce(function (s, c) { return s + c.o.delta; }, 0);
        dishPrice.textContent = fmt(price);
    }
    function renderOptions() {
        optWrap.textContent = '';
        optError.hidden = true;
        var groups = current.options || [];
        optWrap.hidden = groups.length === 0;
        var locked = current.soldOut || current.later;
        groups.forEach(function (g) {
            var box = document.createElement('fieldset');
            box.className = 'm-opt-group';
            box.setAttribute('data-group', g.id);
            var legend = document.createElement('legend');
            var title = document.createElement('span');
            title.textContent = g.name;
            var rule = document.createElement('span');
            rule.className = 'm-opt-rule';
            rule.textContent = (g.min > 0 ? L.required : L.optional) + (g.max > 1 ? ' · ' + L.upTo.replace('{0}', g.max) : '');
            legend.appendChild(title);
            legend.appendChild(rule);
            box.appendChild(legend);
            // Required single choice: round buttons, first one preselected.
            // Optional single choice: a box that can be unticked. Several: tick boxes up to max.
            var radio = g.max === 1 && g.min > 0;
            g.options.forEach(function (o, n) {
                var label = document.createElement('label');
                label.className = 'm-opt';
                var input = document.createElement('input');
                input.type = radio ? 'radio' : 'checkbox';
                input.name = 'opt-' + g.id;
                input.value = o.id;
                input.disabled = locked;
                input.checked = radio && n === 0;
                var name = document.createElement('span');
                name.className = 'm-opt-name';
                name.textContent = o.name;
                var delta = document.createElement('span');
                delta.className = 'm-opt-price tabular';
                delta.textContent = o.delta ? (o.delta > 0 ? '+' : '−') + fmt(Math.abs(o.delta)) : '';
                label.appendChild(input);
                label.appendChild(name);
                label.appendChild(delta);
                box.appendChild(label);
            });
            optWrap.appendChild(box);
        });
        if (groups.length) syncOptions();
    }
    optWrap.addEventListener('change', function (e) {
        var input = e.target;
        var g = (current.options || []).filter(function (x) { return 'opt-' + x.id === input.name; })[0];
        if (g && g.max === 1 && input.type === 'checkbox' && input.checked) {
            groupBox(g).querySelectorAll('input').forEach(function (i) { if (i !== input) i.checked = false; });
        }
        optError.hidden = true;
        syncOptions();
    });

    function syncDishSave() {
        if (current && current.options) { // dishes with choices always add a new line
            dishSave.setAttribute('aria-pressed', 'false');
            dishSave.querySelector('i').className = 'ph ph-plus';
            dish.querySelector('[data-dish-save-label]').textContent = L.save;
            return;
        }
        var on = current && !!find(current.id);
        dishSave.setAttribute('aria-pressed', on ? 'true' : 'false');
        dishSave.querySelector('i').className = on ? 'ph-fill ph-check' : 'ph ph-plus';
        dish.querySelector('[data-dish-save-label]').textContent = on ? L.saved : L.save;
    }
    function openDish(li) {
        current = itemData(li);
        track('dish', current.id);
        dish.querySelector('[data-dish-name]').textContent = current.name;
        var desc = dish.querySelector('[data-dish-desc]');
        desc.textContent = current.desc || '';
        desc.hidden = !current.desc;
        var nutriWrap = dish.querySelector('[data-dish-nutri-wrap]');
        dish.querySelector('[data-dish-nutri]').textContent = current.nutrition || '';
        nutriWrap.hidden = !current.nutrition;
        dish.querySelector('[data-dish-allergens]').textContent = current.allergenText || '';
        var tags = dish.querySelector('[data-dish-tags]');
        tags.textContent = '';
        if (current.tags) tags.appendChild(current.tags.cloneNode(true));
        tags.hidden = !current.tags;
        dishPrice.textContent = current.priceLabel;
        renderOptions();
        if (current.img) { dishImg.src = current.img; dishImg.alt = current.name; dishImg.hidden = false; }
        else { dishImg.hidden = true; dishImg.removeAttribute('src'); }
        dishSave.hidden = current.soldOut || current.later;
        var badgeEl = dish.querySelector('[data-dish-badge]');
        badgeEl.textContent = current.badge || '';
        badgeEl.hidden = !current.badge;
        dishSoldOut.hidden = !current.soldOut;
        syncDishSave();
        openSheet(dish);
    }
    dishSave.addEventListener('click', function () {
        if (!current || current.soldOut || current.later) return;
        if (current.options) {
            var choice = picked();
            var missing = current.options.filter(function (g) {
                return g.min > 0 && choice.filter(function (c) { return c.g === g; }).length < g.min;
            });
            if (missing.length) {
                optError.textContent = L.pleaseChoose.replace('{0}', missing.map(function (g) { return g.name; }).join(', '));
                optError.hidden = false;
                var first = groupBox(missing[0]).querySelector('input');
                if (first) first.focus();
                return;
            }
            addWithOptions(current, choice);
            closeSheet(dish);
            return;
        }
        toggle(current);
        syncDishSave();
    });

    var listSheet = document.querySelector('[data-list-sheet]');
    var listBody = document.querySelector('[data-list-body]');
    var doneBox = document.querySelector('[data-order-done]');
    function showDone(show) {
        if (!doneBox) return;
        doneBox.hidden = !show;
        if (listBody) listBody.hidden = show;
    }
    if (fab) fab.addEventListener('click', function () { showDone(false); render(); openSheet(listSheet); pollOrders(); });
    if (listSheet) listSheet.addEventListener('close', function () { showDone(false); });

    /* ---------- Send to kitchen ---------- */
    var noteEl = document.querySelector('[data-order-note]');
    var errorEl = document.querySelector('[data-order-error]');
    function showError(text) {
        if (!errorEl) return;
        errorEl.textContent = text || '';
        errorEl.hidden = !text;
    }
    function uuid() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        var b = new Uint8Array(16);
        (window.crypto || window.msCrypto).getRandomValues(b);
        b[6] = (b[6] & 15) | 64; b[8] = (b[8] & 63) | 128;
        var h = Array.prototype.map.call(b, function (x) { return (x + 256).toString(16).slice(1); }).join('');
        return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
    }
    function setSending(on) {
        sending = on;
        if (!sendBtn) return;
        sendBtn.disabled = on || list.length === 0;
        sendBtn.querySelector('[data-order-send-label]').textContent = on ? OW.sending : OW.send;
    }
    if (sendBtn) sendBtn.addEventListener('click', function () {
        if (sending || !list.length) return;
        showError('');
        // Sold-out dishes can't be sent: say which, and let the guest remove them.
        var gone = list.filter(function (i) { return isSoldOut(i.id); });
        if (gone.length) {
            showError(OW.notNow.replace('{0}', gone.map(function (i) { return i.name; }).join(', ')));
            return;
        }
        if (!requestId) requestId = uuid();
        var payload = {
            branch: ORDER.branch, table: ORDER.table, code: ORDER.code, requestId: requestId, lang: ORDER.lang,
            note: noteEl ? noteEl.value : '',
            items: list.map(function (i) {
                // key is "dishId" or "dishId:optionId-optionId" (see addWithOptions).
                var parts = i.key.split(':');
                return { id: Number(i.id), qty: i.qty, options: parts[1] ? parts[1].split('-').map(Number) : [] };
            })
        };
        setSending(true);
        fetch(ORDER.orderUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload)
        }).then(function (r) {
            if (r.status === 429) { showError(OW.busy); return; }
            if (r.status !== 200 && r.status !== 422) throw new Error('HTTP ' + r.status);
            return r.json().then(function (data) {
                if (!data.ok) { showError(data.message || OW.unavailable); return; }
                var o = data.order;
                myOrders = myOrders.filter(function (x) { return x.id !== o.id; });
                myOrders.unshift({ id: o.id, n: o.number, table: o.table, status: o.status, statusText: o.statusText, total: o.total, count: o.count, t: Date.now() });
                saveOrders();
                list = [];
                persist();
                if (noteEl) noteEl.value = '';
                document.querySelector('[data-order-done-text]').textContent =
                    (OW.orderNumber || '#{0}').replace('{0}', o.number) + ' · ' + o.statusText;
                showDone(true);
                render();
                schedulePoll();
            });
        }).catch(function () {
            showError(OW.network); // same requestId on retry: the kitchen never gets it twice
        }).then(function () { setSending(false); });
    });

    /* ---------- Order status: polled while this phone has open orders ---------- */
    var pollTimer = null;
    function schedulePoll() {
        clearTimeout(pollTimer);
        if (myOrders.some(function (o) { return !isFinal(o); })) pollTimer = setTimeout(pollOrders, 10000);
    }
    function pollOrders() {
        clearTimeout(pollTimer);
        if (!ORDER.ordersUrl || !myOrders.length || document.visibilityState !== 'visible') return;
        var ids = myOrders.slice(0, 10).map(function (o) { return o.id; }).join(',');
        fetch(ORDER.ordersUrl + '?ids=' + encodeURIComponent(ids) + '&lang=' + encodeURIComponent(ORDER.lang || ''), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (rows) {
                rows.forEach(function (row) {
                    myOrders.forEach(function (o) {
                        if (o.id === row.id) { o.status = row.status; o.statusText = row.statusText; o.total = row.total; o.count = row.count; }
                    });
                });
                // Orders the server no longer knows (deleted with their branch) drop off.
                myOrders = myOrders.filter(function (o) { return rows.some(function (row) { return row.id === o.id; }); });
                saveOrders();
                render();
            })
            .catch(function () { /* try again on the next tick */ })
            .then(schedulePoll);
    }
    document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'visible') pollOrders(); });
    if (myOrders.length) pollOrders();
    var clearBtn = document.querySelector('[data-clear]');
    if (clearBtn) clearBtn.addEventListener('click', function () {
        if (!window.confirm(clearBtn.textContent.trim() + '?')) return;
        list = []; persist(); render(); closeSheet(listSheet);
    });

    /* ---------- Language menu: close on outside click / Escape ---------- */
    var lang = document.querySelector('.m-lang');
    if (lang) {
        lang.querySelectorAll('a[lang]').forEach(function (a) {
            a.addEventListener('click', function () {
                if (a.getAttribute('aria-current') !== 'true') track('lang', null, a.getAttribute('lang'));
            });
        });
        document.addEventListener('click', function (e) { if (!lang.contains(e.target)) lang.open = false; });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') lang.open = false; });
    }

    /* ---------- Opening hours: close on Escape ---------- */
    var hoursBox = document.querySelector('.m-hours');
    if (hoursBox) {
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && hoursBox.open) { hoursBox.open = false; hoursBox.querySelector('summary').focus(); }
        });
    }

    /* ---------- Category chips: scroll-spy with IntersectionObserver ---------- */
    var scroller = document.querySelector('[data-cats-scroll]');
    var chips = {};
    document.querySelectorAll('[data-chip]').forEach(function (c) { chips[c.dataset.chip] = c; });
    var activeChip = null;

    function setActive(id) {
        var chip = chips[id];
        if (!chip || chip === activeChip) return;
        if (activeChip) { activeChip.classList.remove('is-active'); activeChip.removeAttribute('aria-current'); }
        chip.classList.add('is-active');
        chip.setAttribute('aria-current', 'true');
        activeChip = chip;
        var left = chip.offsetLeft - (scroller.clientWidth - chip.offsetWidth) / 2;
        scroller.scrollTo({ left: left, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    }

    var sections = document.querySelectorAll('[data-cat]');
    if ('IntersectionObserver' in window && scroller) {
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (e) { if (e.isIntersecting) setActive(e.target.id); });
        }, { rootMargin: '-30% 0px -60% 0px' });
        sections.forEach(function (s) { io.observe(s); });
    }

    /* ---------- Search ---------- */
    var searchToggle = document.querySelector('[data-search-toggle]');
    var searchBox = document.getElementById('menuSearch');
    var searchInput = document.querySelector('[data-search-input]');
    var noResults = document.querySelector('[data-noresults]');

    /* ---------- Dietary & allergen filters ----------
       "Show only" diets must all be present; "hide containing" allergens must all be
       absent. A dish whose allergens weren't declared (data-allergens="-1") is never
       treated as free of anything. Remembered per menu in this browser. */
    var FILTER_KEY = KEY + '_filters';
    var F = { diets: 0, allergens: 0 };
    try {
        var savedF = JSON.parse(localStorage.getItem(FILTER_KEY) || 'null');
        if (savedF) F = { diets: Number(savedF.diets) || 0, allergens: Number(savedF.allergens) || 0 };
    } catch (e) { /* private mode */ }

    function passesFilters(li) {
        var d = Number(li.dataset.diets) || 0;
        var a = Number(li.dataset.allergens);
        if (F.diets && (d & F.diets) !== F.diets) return false;
        if (F.allergens && (a === -1 || (a & F.allergens) !== 0)) return false;
        return true;
    }

    function apply() {
        var q = searchInput ? searchInput.value.trim().toLowerCase() : '';
        var any = false, shown = 0;
        sections.forEach(function (s) {
            var visible = 0;
            s.querySelectorAll('[data-item]').forEach(function (li) {
                var match = (!q || li.dataset.search.indexOf(q) !== -1) && passesFilters(li);
                li.hidden = !match;
                if (match) visible++;
            });
            s.hidden = visible === 0;
            if (visible) any = true;
            shown += visible;
        });
        if (noResults) noResults.hidden = any;
        // The recommended row isn't filtered; hide it while results are being narrowed down.
        if (featuredRow) featuredRow.hidden = !!q || !!F.diets || !!F.allergens;
        renderFilterUi(shown);
    }

    var featuredRow = document.querySelector('[data-featured-row]');
    if (featuredRow) featuredRow.addEventListener('click', function (e) {
        var card = e.target.closest('[data-feature]');
        if (!card) return;
        var li = document.querySelector('[data-item][data-id="' + card.getAttribute('data-feature') + '"]');
        if (li) openDish(li);
    });

    var filterSheet = document.querySelector('[data-filter-sheet]');
    var filterToggle = document.querySelector('[data-filter-toggle]');
    var filterCount = document.querySelector('[data-filter-count]');
    var filterBar = document.querySelector('[data-filterbar]');
    var filterSummary = document.querySelector('[data-filter-summary]');
    var filterDone = document.querySelector('[data-filter-done]');
    var filterUnknown = document.querySelector('[data-filter-unknown]');
    var dietBoxes = document.querySelectorAll('[data-filter-diet]');
    var allergenBoxes = document.querySelectorAll('[data-filter-allergen]');

    function labelOf(box) { return box.nextElementSibling.textContent.trim(); }
    function bitsCount(n) { var c = 0; while (n) { c += n & 1; n >>>= 1; } return c; }

    function renderFilterUi(shown) {
        if (!filterSheet) return;
        dietBoxes.forEach(function (b) { b.checked = (F.diets & Number(b.value)) !== 0; });
        allergenBoxes.forEach(function (b) { b.checked = (F.allergens & Number(b.value)) !== 0; });

        var active = bitsCount(F.diets) + bitsCount(F.allergens);
        filterCount.hidden = active === 0;
        filterCount.textContent = active;
        filterToggle.setAttribute('aria-pressed', active ? 'true' : 'false');

        var parts = [];
        dietBoxes.forEach(function (b) { if (b.checked) parts.push(labelOf(b)); });
        var avoid = [];
        allergenBoxes.forEach(function (b) { if (b.checked) avoid.push(labelOf(b)); });
        if (avoid.length) parts.push(body.dataset.labelWithout + ': ' + avoid.join(', '));
        filterBar.hidden = active === 0;
        filterSummary.textContent = parts.join(' · ');

        filterDone.textContent = shown === 1 && body.dataset.labelShowOneDish
            ? body.dataset.labelShowOneDish
            : (body.dataset.labelShowDishes || '{0}').replace('{0}', shown);
        filterUnknown.hidden = !(F.allergens && items.some(function (li) { return li.dataset.allergens === '-1'; }));
    }

    function saveFilters() {
        try { localStorage.setItem(FILTER_KEY, JSON.stringify(F)); } catch (e) { /* not fatal */ }
    }

    if (filterSheet) {
        filterToggle.addEventListener('click', function () { openSheet(filterSheet); });
        filterSheet.addEventListener('change', function (e) {
            var box = e.target, bit = Number(box.value);
            if (box.hasAttribute('data-filter-diet')) F.diets = box.checked ? F.diets | bit : F.diets & ~bit;
            if (box.hasAttribute('data-filter-allergen')) F.allergens = box.checked ? F.allergens | bit : F.allergens & ~bit;
            saveFilters(); apply();
        });
        document.querySelectorAll('[data-filter-reset]').forEach(function (btn) {
            btn.addEventListener('click', function () { F = { diets: 0, allergens: 0 }; saveFilters(); apply(); });
        });
    } else {
        F = { diets: 0, allergens: 0 }; // nothing declared on this menu: never filter
    }

    if (searchToggle && searchBox) {
        searchToggle.addEventListener('click', function () {
            var open = searchBox.hidden;
            searchBox.hidden = !open;
            searchToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (open) { searchInput.focus(); }
            else { searchInput.value = ''; apply(); }
        });
        searchInput.addEventListener('input', apply);
        searchInput.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { searchToggle.click(); searchToggle.focus(); }
        });
    }

    /* ---------- Guest feedback: stars in the footer, details in a sheet ---------- */
    (function () {
        var cfgEl = document.getElementById('feedbackConfig');
        var box = document.querySelector('[data-rate]');
        var sheet = document.querySelector('[data-rate-sheet]');
        if (!cfgEl || !box || !sheet) return;
        var C = JSON.parse(cfgEl.textContent), W = C.words;
        var KEY = 'menu_feedback_' + C.branch, HOLD = 12 * 3600 * 1000; // one rating per visit
        var form = sheet.querySelector('[data-rate-form]');
        var thanks = sheet.querySelector('[data-rate-thanks]');
        var sendBtn = sheet.querySelector('[data-rate-send]');
        var rating = 0, openedAt = 0, busy = false;

        function sentRecently() {
            try { return Date.now() - Number(localStorage.getItem(KEY) || 0) < HOLD; } catch (e) { return false; }
        }
        function showDone() {
            box.querySelector('.m-rate-stars').hidden = true;
            box.querySelector('h2').hidden = true;
            box.querySelector('[data-rate-done]').hidden = false;
        }
        if (sentRecently()) showDone();

        function setRating(n) {
            rating = n;
            sheet.querySelectorAll('[data-pick]').forEach(function (b) {
                var on = Number(b.getAttribute('data-pick')) <= n;
                b.classList.toggle('is-on', on);
                b.setAttribute('aria-checked', Number(b.getAttribute('data-pick')) === n ? 'true' : 'false');
            });
            var low = n <= C.low;
            sheet.querySelector('[data-rate-title]').textContent = low ? W.LowTitle : W.HighTitle;
            sheet.querySelector('[data-rate-private]').hidden = !low;
            sheet.querySelector('[data-rate-contact]').hidden = !low;
        }
        box.querySelectorAll('[data-star]').forEach(function (b) {
            b.addEventListener('mouseenter', function () {
                var n = Number(b.getAttribute('data-star'));
                box.querySelectorAll('[data-star]').forEach(function (x) { x.classList.toggle('is-on', Number(x.getAttribute('data-star')) <= n); });
            });
            b.addEventListener('mouseleave', function () { box.querySelectorAll('[data-star]').forEach(function (x) { x.classList.remove('is-on'); }); });
            b.addEventListener('click', function () {
                form.hidden = false; thanks.hidden = true;
                sheet.querySelector('[data-rate-error]').hidden = true;
                setRating(Number(b.getAttribute('data-star')));
                openedAt = Date.now();
                if (typeof sheet.showModal === 'function') sheet.showModal(); else sheet.setAttribute('open', '');
                sheet.querySelector('textarea').focus();
            });
        });
        sheet.querySelectorAll('[data-pick]').forEach(function (b) {
            b.addEventListener('click', function () { setRating(Number(b.getAttribute('data-pick'))); });
        });

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            if (busy || !rating) return;
            busy = true; sendBtn.disabled = true;
            sendBtn.querySelector('[data-rate-send-label]').textContent = W.Sending;
            var data = new FormData(form);
            data.set('b', C.branch); data.set('rating', rating); data.set('lang', C.lang || '');
            data.set('ms', String(Date.now() - openedAt));
            if (C.table) data.set('t', C.table);
            if (rating > C.low) data.delete('contact');
            fetch(C.url, { method: 'POST', body: new URLSearchParams(data), headers: { 'Accept': 'application/json' } })
                .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
                .then(function (res) {
                    try { localStorage.setItem(KEY, String(Date.now())); } catch (e2) { /* not fatal */ }
                    form.hidden = true; thanks.hidden = false;
                    thanks.querySelector('[data-rate-thanks-title]').textContent = rating <= C.low ? W.ThanksLow : W.ThanksHigh;
                    var g = thanks.querySelector('[data-rate-google]');
                    g.hidden = !res.google; thanks.querySelector('[data-rate-google-ask]').hidden = !res.google;
                    if (res.google) g.href = res.google;
                    showDone();
                    form.reset();
                })
                .catch(function () { sheet.querySelector('[data-rate-error]').hidden = false; })
                .then(function () { busy = false; sendBtn.disabled = false; sendBtn.querySelector('[data-rate-send-label]').textContent = W.Send; });
        });
    })();

    /* ---------- Offline-ready: service worker + "saved copy" note ---------- */
    (function () {
        var note = document.querySelector('[data-offline]');
        var rendered = Date.parse(body.dataset.renderedAt || '');
        function savedAt() {
            try { return new Date(rendered).toLocaleTimeString(document.documentElement.lang || undefined, { hour: '2-digit', minute: '2-digit' }); }
            catch (e) { return ''; }
        }
        function sync() {
            if (!note) return;
            // A copy older than a few minutes, opened fresh (not via Back), came from the saved menus.
            var nav = (performance.getEntriesByType && performance.getEntriesByType('navigation')[0]) || {};
            var old = rendered && Date.now() - rendered > 5 * 60 * 1000 && nav.type !== 'back_forward';
            var offline = navigator.onLine === false || old;
            note.hidden = !offline;
            if (offline) note.querySelector('[data-offline-text]').textContent = (body.dataset.labelOffline || '').replace('{0}', savedAt());
        }
        window.addEventListener('offline', sync);
        window.addEventListener('online', function () { if (note) note.hidden = true; });
        sync();

        if (!('serviceWorker' in navigator)) return;
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js', { scope: '/menu' }).then(function () {
                return navigator.serviceWorker.ready;
            }).then(function (reg) {
                // The styles, scripts and fonts this page loaded before the worker was in charge
                // (a guest's first visit), so the saved copy looks the same offline.
                var assets = [];
                (performance.getEntriesByType ? performance.getEntriesByType('resource') : []).forEach(function (e) {
                    if (/^(link|css|script)$/.test(e.initiatorType) && assets.indexOf(e.name) < 0) assets.push(e.name);
                });
                // Keep this menu's dish photos too, before the guest scrolls to them.
                // Not on "data saver" connections.
                var urls = [];
                if (!(navigator.connection && navigator.connection.saveData)) {
                    document.querySelectorAll('[data-item][data-img], img[src]').forEach(function (el) {
                        var u = el.getAttribute('data-img') || el.getAttribute('src');
                        if (u && urls.indexOf(u) < 0) urls.push(u);
                    });
                }
                if (reg.active && (urls.length || assets.length)) reg.active.postMessage({ type: 'warm', urls: urls, assets: assets });
            }).catch(function () { /* not available (private mode, old browser): the menu works as before */ });
        });
    })();

    load();
    render();
    apply();
})();
