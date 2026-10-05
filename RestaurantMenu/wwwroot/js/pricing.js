/* Pricing page: recalculates the total as boxes change. Without JavaScript the "Update price"
   button reloads the page with the same sums (PricingRules on the server); this mirrors them. */
(function () {
    var root = document.querySelector('[data-pricing]');
    if (!root) return;
    var prices = JSON.parse(root.dataset.prices || '{}');
    var currency = root.dataset.currency;
    var form = root.querySelector('[data-pricing-form]');
    var boxes = Array.prototype.slice.call(form.querySelectorAll('[data-module]'));
    var branchesInput = form.querySelector('[data-branches]');
    var update = form.querySelector('[data-update-button]');
    if (update) update.hidden = true;

    function money(cents) {
        var whole = cents % 100 === 0;
        var n = (cents / 100).toFixed(whole ? 0 : 2);
        if (currency === 'EUR') return '€' + n;
        if (currency === 'USD') return '$' + n;
        return Number(n).toLocaleString('en-US', { minimumFractionDigits: whole ? 0 : 2 }) + ' ' + currency;
    }
    function price(module, interval) { return prices[module] && prices[module][interval] != null ? prices[module][interval] : null; }
    function interval() { var r = form.querySelector('[data-interval]:checked'); return r ? r.value : 'Month'; }
    function branches() { var n = parseInt(branchesInput.value, 10); return isNaN(n) ? 1 : Math.min(50, Math.max(1, n)); }
    function nameOf(box) { return box.closest('label').querySelector('strong').textContent; }

    function render() {
        // A module whose base is off can't be on (own domain needs the website).
        boxes.forEach(function (b) {
            var needs = b.dataset.needs && form.querySelector('[data-module="' + b.dataset.needs + '"]');
            if (needs) { b.disabled = !needs.checked; if (!needs.checked) b.checked = false; }
        });
        var i = interval(), n = branches();
        var per = i === 'Year' ? root.dataset.perYear : root.dataset.perMonth;
        var lines = [{ name: root.querySelector('.module.is-fixed strong').textContent, module: 'Menu', qty: n }];
        boxes.forEach(function (b) {
            if (b.checked) lines.push({ name: nameOf(b), module: b.dataset.module, qty: b.dataset.perBranch === '1' ? n : 1 });
        });
        root.querySelectorAll('[data-unit]').forEach(function (el) {
            var p = price(el.dataset.unit, i);
            el.textContent = p == null ? '' : money(p) + ' ' + per + ' ' + el.dataset.suffix;
        });

        var total = 0, complete = true, monthly = 0, monthlyComplete = true;
        var list = root.querySelector('[data-lines]');
        list.innerHTML = '';
        lines.forEach(function (l) {
            var unit = price(l.module, i), m = price(l.module, 'Month');
            if (unit == null) complete = false; else total += unit * l.qty;
            if (m == null) monthlyComplete = false; else monthly += m * l.qty * 12;
            var li = document.createElement('li');
            var a = document.createElement('span'); a.textContent = l.name + (l.qty > 1 ? ' × ' + l.qty : '');
            var b = document.createElement('span'); b.className = 'tabular'; b.textContent = unit == null ? '–' : money(unit * l.qty);
            li.appendChild(a); li.appendChild(b); list.appendChild(li);
        });
        var totalEl = root.querySelector('[data-total]');
        totalEl.innerHTML = '';
        var strong = document.createElement('strong');
        if (complete) {
            strong.className = 'tabular'; strong.textContent = money(total);
            totalEl.appendChild(strong); totalEl.appendChild(document.createTextNode(' ' + per));
        } else {
            strong.textContent = root.dataset.onRequest; totalEl.appendChild(strong);
        }
        var saving = root.querySelector('[data-saving-line]');
        var save = i === 'Year' && complete && monthlyComplete && monthly > total ? monthly - total : 0;
        saving.hidden = !save;
        saving.textContent = save ? root.dataset.saving.replace('{0}', money(save)) : '';

        var q = boxes.filter(function (b) { return b.checked; }).map(function (b) { return 'm=' + b.dataset.module; });
        q.push('b=' + n, 'i=' + i);
        var lang = root.dataset.lang === 'en' ? '' : '&lang=' + root.dataset.lang;
        root.querySelector('[data-start]').href = '/signup?' + q.join('&') + lang;
        try { history.replaceState(null, '', '/pricing?' + q.join('&') + lang + '#build'); } catch (e) { /* file:// or old browsers */ }
    }

    form.addEventListener('change', render);
    branchesInput.addEventListener('input', render);
    render();
})();
