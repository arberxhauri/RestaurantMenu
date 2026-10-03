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
            soldOut: li.dataset.soldout === 'true',
            later: li.dataset.later === 'true',
            allergenText: li.dataset.allergenText,
            tags: li.querySelector('[data-tags]')
        };
    }

    function toggle(data) {
        var existing = find(data.id);
        if (existing) list = list.filter(function (i) { return i.id !== data.id; });
        else { list.push({ id: data.id, name: data.name, price: data.price, qty: 1 }); track('add', data.id); }
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
        dish.querySelector('[data-dish-price]').textContent = current.priceLabel;
        if (current.img) { dishImg.src = current.img; dishImg.alt = current.name; dishImg.hidden = false; }
        else { dishImg.hidden = true; dishImg.removeAttribute('src'); }
        dishSave.hidden = current.soldOut || current.later;
        dishSoldOut.hidden = !current.soldOut;
        syncDishSave();
        openSheet(dish);
    }
    dishSave.addEventListener('click', function () {
        if (!current || current.soldOut || current.later) return;
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
        renderFilterUi(shown);
    }

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

    load();
    render();
    apply();
})();
