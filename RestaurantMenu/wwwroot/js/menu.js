/* Public menu: category scroll-spy, search, dish sheet, and the guest's saved list.
   The list lives only in this guest's browser (localStorage). */
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
        soldOut: body.dataset.labelSoldout
    };
    // Every sold-out dish, including ones the restaurant hides, so a guest's saved list can flag them.
    var SOLD_OUT = {};
    (body.dataset.soldoutIds || '').split(',').forEach(function (id) { if (id) SOLD_OUT[id] = true; });
    function isSoldOut(id) { return SOLD_OUT[String(id)] === true; }

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
            list = Array.isArray(parsed) ? parsed.map(function (i) {
                return { id: String(i.id), name: String(i.name), price: Number(i.price) || 0, qty: Math.max(1, Number(i.qty) || 1) };
            }) : [];
        } catch (e) { list = []; }
    }
    function persist() {
        try { localStorage.setItem(KEY, JSON.stringify(list)); } catch (e) { /* not fatal */ }
    }
    function find(id) { return list.find(function (i) { return i.id === id; }); }

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
            soldOut: li.dataset.soldout === 'true'
        };
    }

    function toggle(data) {
        var existing = find(data.id);
        if (existing) list = list.filter(function (i) { return i.id !== data.id; });
        else list.push({ id: data.id, name: data.name, price: data.price, qty: 1 });
        persist();
        render();
    }

    /* ---------- Rendering ---------- */
    var fab = document.querySelector('[data-fab]');
    var fabCount = document.querySelector('[data-fab-count]');
    var fabTotal = document.querySelector('[data-fab-total]');
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
            var on = !!find(li.dataset.id);
            btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            btn.setAttribute('aria-label', (on ? L.saved : L.save) + ': ' + li.dataset.name);
            btn.innerHTML = on ? '<i class="ph-fill ph-check" aria-hidden="true"></i>' : '<i class="ph ph-plus" aria-hidden="true"></i>';
        });

        if (fab) {
            fab.hidden = list.length === 0;
            fabCount.textContent = count();
            fabTotal.textContent = fmt(total());
        }

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
    var current = null;

    function syncDishSave() {
        var on = current && !!find(current.id);
        dishSave.setAttribute('aria-pressed', on ? 'true' : 'false');
        dishSave.querySelector('i').className = on ? 'ph-fill ph-check' : 'ph ph-plus';
        dish.querySelector('[data-dish-save-label]').textContent = on ? L.saved : L.save;
    }
    function openDish(li) {
        current = itemData(li);
        dish.querySelector('[data-dish-name]').textContent = current.name;
        var desc = dish.querySelector('[data-dish-desc]');
        desc.textContent = current.desc || '';
        desc.hidden = !current.desc;
        var nutriWrap = dish.querySelector('[data-dish-nutri-wrap]');
        dish.querySelector('[data-dish-nutri]').textContent = current.nutrition || '';
        nutriWrap.hidden = !current.nutrition;
        dish.querySelector('[data-dish-price]').textContent = current.priceLabel;
        if (current.img) { dishImg.src = current.img; dishImg.alt = current.name; dishImg.hidden = false; }
        else { dishImg.hidden = true; dishImg.removeAttribute('src'); }
        dishSave.hidden = current.soldOut;
        dishSoldOut.hidden = !current.soldOut;
        syncDishSave();
        openSheet(dish);
    }
    dishSave.addEventListener('click', function () {
        if (!current || current.soldOut) return;
        toggle(current);
        syncDishSave();
    });

    var listSheet = document.querySelector('[data-list-sheet]');
    if (fab) fab.addEventListener('click', function () { render(); openSheet(listSheet); });
    var clearBtn = document.querySelector('[data-clear]');
    if (clearBtn) clearBtn.addEventListener('click', function () {
        if (!window.confirm(clearBtn.textContent.trim() + '?')) return;
        list = []; persist(); render(); closeSheet(listSheet);
    });

    /* ---------- Language menu: close on outside click / Escape ---------- */
    var lang = document.querySelector('.m-lang');
    if (lang) {
        document.addEventListener('click', function (e) { if (!lang.contains(e.target)) lang.open = false; });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') lang.open = false; });
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

    function filter(q) {
        q = q.trim().toLowerCase();
        var any = false;
        sections.forEach(function (s) {
            var visible = 0;
            s.querySelectorAll('[data-item]').forEach(function (li) {
                var match = !q || li.dataset.search.indexOf(q) !== -1;
                li.hidden = !match;
                if (match) visible++;
            });
            s.hidden = visible === 0;
            if (visible) any = true;
        });
        if (noResults) noResults.hidden = any;
    }

    if (searchToggle && searchBox) {
        searchToggle.addEventListener('click', function () {
            var open = searchBox.hidden;
            searchBox.hidden = !open;
            searchToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (open) { searchInput.focus(); }
            else { searchInput.value = ''; filter(''); }
        });
        searchInput.addEventListener('input', function () { filter(searchInput.value); });
        searchInput.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { searchToggle.click(); searchToggle.focus(); }
        });
    }

    load();
    render();
})();
