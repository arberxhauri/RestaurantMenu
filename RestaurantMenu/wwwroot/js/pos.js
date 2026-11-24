/* The POS screen (docs/PLAN-backoffice.md, phase 6). The server prices, saves and checks everything
   (PosController, PosService, PosRules); this script only collects the order and shows the bills. */
(function () {
    'use strict';

    var body = document.body;
    var DATA_URL = body.dataset.pData;
    var BASE = body.dataset.pBase;
    var CURRENCY = body.dataset.pCurrency || '';
    var METHOD = { Cash: 0, Card: 1 };
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var TOKEN = tokenInput ? tokenInput.value : '';

    function $(s, root) { return (root || document).querySelector(s); }
    function $$(s, root) { return Array.prototype.slice.call((root || document).querySelectorAll(s)); }
    function money(n) { return (CURRENCY ? CURRENCY + ' ' : '') + (Number(n) || 0).toFixed(2); }
    function round2(n) { return Math.round((Number(n) || 0) * 100) / 100; }
    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text != null) e.textContent = text;
        return e;
    }
    function uuid() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 3 | 8)).toString(16);
        });
    }
    function toast(message, bad) {
        var t = $('[data-p-toast]');
        t.textContent = message;
        t.classList.toggle('is-bad', !!bad);
        t.hidden = false;
        clearTimeout(toast.timer);
        toast.timer = setTimeout(function () { t.hidden = true; }, 4500);
    }
    function post(path, fields) {
        var form = new FormData();
        form.append('__RequestVerificationToken', TOKEN);
        Object.keys(fields).forEach(function (k) { if (fields[k] != null) form.append(k, fields[k]); });
        return fetch(BASE + path, { method: 'POST', body: form, credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                return r.json().catch(function () { return {}; }).then(function (json) {
                    if (!r.ok) {
                        var err = new Error(json.message || 'That didn\'t work. Check the connection and try again.');
                        err.data = json;
                        throw err;
                    }
                    return json;
                });
            });
    }

    // ---------- State ----------
    var categories = [];
    var tables = [];
    var discounts = [];
    var bills = [];
    var activeCat = 'all';
    var cart = newCart();
    var selected = null; // key of the open bill shown
    var tenders = [];

    function newCart() {
        return { requestId: uuid(), type: 'dinein', lines: [] };
    }

    // ---------- Phone tabs ----------
    function show(section) {
        $('.pos-shell').dataset.pShow = section;
        $$('[data-p-tab]').forEach(function (b) { b.setAttribute('aria-pressed', b.dataset.pTab === section ? 'true' : 'false'); });
    }
    $$('[data-p-tab]').forEach(function (b) { b.addEventListener('click', function () { show(b.dataset.pTab); }); });

    // ---------- Menu ----------
    function renderCats() {
        var box = $('[data-p-cats]');
        box.textContent = '';
        [{ id: 'all', name: 'All' }].concat(categories).forEach(function (c) {
            var b = el('button', 'pos-cat', c.name);
            b.type = 'button';
            b.setAttribute('role', 'tab');
            b.setAttribute('aria-selected', String(c.id) === String(activeCat) ? 'true' : 'false');
            b.addEventListener('click', function () { activeCat = c.id; renderCats(); renderGrid(); });
            box.appendChild(b);
        });
    }

    function renderGrid() {
        var grid = $('[data-p-grid]');
        var q = ($('[data-p-search]').value || '').trim().toLowerCase();
        grid.textContent = '';
        categories.forEach(function (c) {
            if (activeCat !== 'all' && String(c.id) !== String(activeCat)) return;
            c.items.forEach(function (p) {
                if (q && p.name.toLowerCase().indexOf(q) < 0) return;
                var b = el('button', 'pos-item' + (p.available ? '' : ' is-off'));
                b.type = 'button';
                b.disabled = !p.available;
                b.appendChild(el('span', 'pos-item-name', p.name));
                b.appendChild(el('span', 'pos-item-price', p.available ? money(p.price) : 'Sold out'));
                if (p.groups.length) b.appendChild(el('span', 'pos-item-opts', 'Options'));
                b.addEventListener('click', function () { choose(p); });
                grid.appendChild(b);
            });
        });
        if (!grid.children.length) grid.appendChild(el('p', 'pos-empty', 'No dishes match.'));
    }
    $('[data-p-search]').addEventListener('input', renderGrid);

    // ---------- Options dialog ----------
    var dialog = $('[data-p-options]');
    var pending = null;

    function choose(p) {
        var withOptions = p.groups.filter(function (g) { return g.options.length; });
        if (!withOptions.length) { addLine(p, []); return; }
        pending = p;
        $('[data-p-options-title]').textContent = p.name;
        $('[data-p-options-error]').textContent = '';
        var bodyEl = $('[data-p-options-body]');
        bodyEl.textContent = '';
        withOptions.forEach(function (g) {
            var fs = el('fieldset', 'pos-group');
            var hint = g.min > 0 ? (g.max === 1 ? 'choose 1' : 'choose ' + g.min + '–' + g.max) : (g.max === 1 ? 'optional' : 'up to ' + g.max);
            fs.appendChild(el('legend', null, g.name + ' (' + hint + ')'));
            g.options.forEach(function (o, i) {
                var label = el('label', 'pos-option');
                var input = document.createElement('input');
                input.type = g.max === 1 ? 'radio' : 'checkbox';
                input.name = 'g' + g.id;
                input.value = o.id;
                if (g.max === 1 && g.min > 0 && i === 0) input.checked = true;
                label.appendChild(input);
                label.appendChild(el('span', null, o.name));
                if (o.delta) label.appendChild(el('small', null, (o.delta > 0 ? '+' : '') + money(o.delta)));
                fs.appendChild(label);
            });
            bodyEl.appendChild(fs);
        });
        dialog.showModal();
    }

    $('[data-p-options-add]').addEventListener('click', function () {
        var p = pending;
        if (!p) return;
        var chosen = [];
        var problem = null;
        p.groups.forEach(function (g) {
            if (!g.options.length) return;
            var ids = $$('input[name="g' + g.id + '"]:checked', dialog).map(function (i) { return Number(i.value); });
            if (ids.length < g.min || ids.length > g.max) problem = 'Choose ' + (g.min === g.max ? g.min : g.min + '–' + g.max) + ' for ' + g.name + '.';
            chosen = chosen.concat(ids);
        });
        if (problem) { $('[data-p-options-error]').textContent = problem; return; }
        dialog.close();
        addLine(p, chosen);
    });

    // ---------- Cart ----------
    function optionInfo(p, ids) {
        var names = [];
        var delta = 0;
        p.groups.forEach(function (g) {
            g.options.forEach(function (o) {
                if (ids.indexOf(o.id) >= 0) { names.push(o.name); delta += o.delta; }
            });
        });
        return { text: names.join(', '), delta: delta };
    }

    function addLine(p, ids) {
        var sorted = ids.slice().sort(function (a, b) { return a - b; });
        var key = p.id + ':' + sorted.join(',');
        var line = cart.lines.filter(function (l) { return l.key === key; })[0];
        if (line) line.qty = Math.min(line.qty + 1, 20);
        else {
            var info = optionInfo(p, sorted);
            cart.lines.push({ key: key, id: p.id, name: p.name, options: sorted, optText: info.text, unit: round2(p.price + info.delta), qty: 1 });
        }
        showCart();
        renderCart();
    }

    function renderCart() {
        var list = $('[data-p-lines]');
        list.textContent = '';
        var total = 0;
        cart.lines.forEach(function (l) {
            total += l.unit * l.qty;
            var li = el('li', 'pos-line');
            var text = el('div', 'pos-line-text');
            text.appendChild(el('strong', null, l.name));
            if (l.optText) text.appendChild(el('small', null, l.optText));
            li.appendChild(text);
            var stepper = el('div', 'pos-stepper');
            var minus = el('button', 'pos-step', '−');
            minus.type = 'button';
            minus.setAttribute('aria-label', 'One less ' + l.name);
            minus.addEventListener('click', function () {
                l.qty -= 1;
                if (l.qty <= 0) cart.lines = cart.lines.filter(function (x) { return x !== l; });
                renderCart();
            });
            var plus = el('button', 'pos-step', '+');
            plus.type = 'button';
            plus.setAttribute('aria-label', 'One more ' + l.name);
            plus.addEventListener('click', function () { l.qty = Math.min(l.qty + 1, 20); renderCart(); });
            stepper.appendChild(minus);
            stepper.appendChild(el('span', 'pos-qty', String(l.qty)));
            stepper.appendChild(plus);
            li.appendChild(stepper);
            li.appendChild(el('span', 'pos-line-total', money(l.unit * l.qty)));
            list.appendChild(li);
        });
        $('[data-p-lines-empty]').hidden = cart.lines.length > 0;
        $('[data-p-cart-total]').textContent = money(total);
        $('[data-p-send]').disabled = cart.lines.length === 0;
    }

    function setType(type) {
        cart.type = type;
        $$('[data-p-type]').forEach(function (b) { b.setAttribute('aria-pressed', b.dataset.pType === type ? 'true' : 'false'); });
        $('[data-p-dinein]').hidden = type !== 'dinein';
    }
    $$('[data-p-type]').forEach(function (b) { b.addEventListener('click', function () { setType(b.dataset.pType); }); });

    function renderTables() {
        var select = $('[data-p-table]');
        tables.forEach(function (t) {
            var o = document.createElement('option');
            o.value = t.id;
            o.textContent = 'Table ' + t.number + (t.name ? ' · ' + t.name : '') + (t.area ? ' (' + t.area + ')' : '');
            select.appendChild(o);
        });
    }

    $('[data-p-send]').addEventListener('click', function () {
        var btn = this;
        var tableId = $('[data-p-table]').value;
        if (cart.type === 'dinein' && !tableId) { toast('Choose a table, or switch to takeaway.', true); return; }
        btn.disabled = true;
        var payload = {
            requestId: cart.requestId, type: cart.type, tableId: cart.type === 'dinein' ? Number(tableId) : null,
            guests: Number($('[data-p-guests]').value) || 0, note: $('[data-p-note]').value,
            items: cart.lines.map(function (l) { return { id: l.id, qty: l.qty, options: l.options }; })
        };
        post('orders', { payload: JSON.stringify(payload) })
            .then(function (json) {
                toast('Order #' + json.number + ' sent to the kitchen and bar.');
                cart = newCart();
                $('[data-p-note]').value = '';
                setType('dinein');
                bills = json.bills || bills;
                renderBills();
                renderCart();
            })
            .catch(function (e) { toast(e.message, true); btn.disabled = false; });
    });

    // ---------- Bills ----------
    function since(utc) {
        var minutes = Math.max(0, Math.round((Date.now() - new Date(utc).getTime()) / 60000));
        return minutes < 60 ? minutes + ' min' : Math.floor(minutes / 60) + ' h ' + (minutes % 60) + ' min';
    }

    function renderBills() {
        var list = $('[data-p-bills]');
        list.textContent = '';
        bills.forEach(function (b) {
            var card = el('button', 'pos-bill' + (b.key === selected ? ' is-active' : '') + (b.billRequested ? ' is-requested' : ''));
            card.type = 'button';
            var top = el('span', 'pos-bill-top');
            top.appendChild(el('strong', null, b.label));
            top.appendChild(el('span', 'pos-bill-total', money(b.total)));
            card.appendChild(top);
            var count = b.lines.reduce(function (n, l) { return n + l.qty; }, 0);
            card.appendChild(el('small', null, count + ' items · ' + since(b.openedUtc) + (b.guests ? ' · ' + b.guests + ' guests' : '')));
            var tags = el('span', 'pos-tags');
            if (b.billRequested) tags.appendChild(el('span', 'pos-tag pos-tag--warn', 'Bill requested'));
            if (b.allServed) tags.appendChild(el('span', 'pos-tag pos-tag--ok', 'Served'));
            if (b.discountName) tags.appendChild(el('span', 'pos-tag', b.discountName));
            card.appendChild(tags);
            card.addEventListener('click', function () { selectBill(b.key); });
            list.appendChild(card);
        });
        $('[data-p-bills-empty]').hidden = bills.length > 0;
        $('[data-p-count]').textContent = bills.length;
        if (selected) {
            var still = currentBill();
            if (still) fillBill(still); else showCart();
        }
    }

    function currentBill() { return bills.filter(function (b) { return b.key === selected; })[0]; }

    function showCart() {
        selected = null;
        $('[data-p-bill]').hidden = true;
        $('[data-p-cart]').hidden = false;
        $$('.pos-bill.is-active').forEach(function (c) { c.classList.remove('is-active'); });
    }

    function selectBill(key) {
        selected = key;
        var bill = currentBill();
        if (!bill) return;
        tenders = [{ method: 'Cash', amount: bill.total }];
        $('[data-p-tip]').value = '0';
        $('[data-p-cart]').hidden = true;
        $('[data-p-bill]').hidden = false;
        renderBills();
        show('ticket');
    }

    function fillBill(b) {
        $('[data-p-bill-title]').textContent = b.label;
        $('[data-p-bill-meta]').textContent = 'Orders #' + b.orderNumbers.join(', #') + ' · open ' + since(b.openedUtc);
        var list = $('[data-p-bill-lines]');
        list.textContent = '';
        b.lines.forEach(function (l) {
            var li = el('li', 'pos-line');
            var text = el('div', 'pos-line-text');
            text.appendChild(el('strong', null, l.qty + ' × ' + l.name));
            if (l.options) text.appendChild(el('small', null, l.options));
            li.appendChild(text);
            li.appendChild(el('span', 'pos-line-total', money(l.total)));
            list.appendChild(li);
        });
        var match = discounts.filter(function (d) { return d.name === b.discountName; })[0];
        $('[data-p-discount]').value = match ? String(match.id) : '';
        $('[data-p-sub]').textContent = money(b.subtotal);
        $('[data-p-off]').textContent = b.discount ? '− ' + money(b.discount) : money(0);
        $('[data-p-due]').textContent = money(b.total);
        var req = $('[data-p-request]');
        req.setAttribute('aria-pressed', b.billRequested ? 'true' : 'false');
        req.querySelector('span').textContent = b.billRequested ? 'Bill requested ✓' : 'Bill requested';
        $('[data-p-print]').href = BASE + 'bills/' + b.key + '/print';
        renderTenders(b);
    }

    function renderTenders(b) {
        var box = $('[data-p-tenders]');
        box.textContent = '';
        tenders.forEach(function (t, i) {
            var row = el('div', 'pos-tender');
            row.appendChild(el('span', 'pos-tender-method', t.method));
            var input = document.createElement('input');
            input.type = 'number';
            input.min = '0';
            input.step = '0.01';
            input.inputMode = 'decimal';
            input.value = t.amount;
            input.setAttribute('aria-label', t.method + ' amount');
            input.addEventListener('input', function () { t.amount = Number(input.value) || 0; updateChange(b); });
            row.appendChild(input);
            var remove = el('button', 'pos-step', '×');
            remove.type = 'button';
            remove.setAttribute('aria-label', 'Remove ' + t.method);
            remove.addEventListener('click', function () { tenders.splice(i, 1); renderTenders(b); });
            row.appendChild(remove);
            box.appendChild(row);
        });
        updateChange(b);
    }

    function updateChange(b) {
        var paid = tenders.reduce(function (n, t) { return n + (Number(t.amount) || 0); }, 0);
        var diff = round2(paid - b.total);
        var out = $('[data-p-change]');
        out.textContent = diff > 0 ? 'Change to give: ' + money(diff) : diff < 0 ? 'Still to pay: ' + money(-diff) : 'Exact amount.';
        out.classList.toggle('is-short', diff < 0);
    }

    $$('[data-p-add-tender]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var b = currentBill();
            if (!b) return;
            var paid = tenders.reduce(function (n, t) { return n + (Number(t.amount) || 0); }, 0);
            tenders.push({ method: btn.dataset.pAddTender, amount: Math.max(0, round2(b.total - paid)) });
            renderTenders(b);
        });
    });

    function replaceBill(bill) {
        bills = bills.map(function (b) { return b.key === bill.key ? bill : b; });
        renderBills();
    }

    $('[data-p-discount]').addEventListener('change', function () {
        var key = selected;
        if (!key) return;
        post('bills/' + key + '/discount', { discountId: this.value || '' })
            .then(function (json) {
                tenders = [{ method: 'Cash', amount: json.bill.total }];
                replaceBill(json.bill);
            })
            .catch(function (e) { toast(e.message, true); if (e.data && e.data.bill) replaceBill(e.data.bill); });
    });

    $('[data-p-request]').addEventListener('click', function () {
        var b = currentBill();
        if (!b) return;
        post('bills/' + b.key + '/request', { requested: String(!b.billRequested) })
            .then(function (json) { replaceBill(json.bill); })
            .catch(function (e) { toast(e.message, true); });
    });

    $('[data-p-add-items]').addEventListener('click', function () {
        var b = currentBill();
        cart = newCart();
        if (b && b.tableId) {
            setType('dinein');
            $('[data-p-table]').value = String(b.tableId);
        } else {
            setType('takeaway');
        }
        renderCart();
        showCart();
        show('menu');
    });

    $('[data-p-pay]').addEventListener('click', function () {
        var btn = this;
        var b = currentBill();
        if (!b) return;
        var payload = {
            tenders: tenders.filter(function (t) { return Number(t.amount) > 0; })
                .map(function (t) { return { method: METHOD[t.method], amount: round2(t.amount) }; }),
            tip: round2($('[data-p-tip]').value)
        };
        btn.disabled = true;
        post('bills/' + b.key + '/pay', { payload: JSON.stringify(payload) })
            .then(function (json) {
                toast(b.label + ' paid' + (json.change > 0 ? '. Give back ' + money(json.change) + '.' : '.'));
                bills = json.bills || bills.filter(function (x) { return x.key !== b.key; });
                showCart();
                renderBills();
            })
            .catch(function (e) { toast(e.message, true); })
            .then(function () { btn.disabled = false; });
    });

    $('[data-p-new]').addEventListener('click', function () { showCart(); show('menu'); });

    // ---------- Loading ----------
    function load() {
        return fetch(DATA_URL, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error('The menu couldn\'t be loaded. Reload the page.'); return r.json(); })
            .then(function (data) {
                categories = data.categories.filter(function (c) { return c.items.length; });
                tables = data.tables;
                discounts = data.discounts;
                bills = data.bills;
                var select = $('[data-p-discount]');
                discounts.forEach(function (d) {
                    var o = document.createElement('option');
                    o.value = d.id;
                    o.textContent = d.name;
                    select.appendChild(o);
                });
                renderTables();
                renderCats();
                renderGrid();
                renderCart();
                renderBills();
            })
            .catch(function (e) { toast(e.message, true); });
    }

    function refreshBills() {
        if (document.hidden) return;
        fetch(BASE + 'bills', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (json) { if (json) { bills = json.bills; renderBills(); } })
            .catch(function () { /* the next refresh tries again */ });
    }

    load();
    setInterval(refreshBills, 15000);
})();
