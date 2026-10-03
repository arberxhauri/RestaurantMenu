/* Back office behaviour. Everything is opt-in through data attributes so the
   views stay declarative. */
(function () {
    'use strict';

    /* ---------- Lightweight toast ---------- */
    var toastEl = null, toastTimer = null;
    function toast(message) {
        if (!toastEl) {
            toastEl = document.createElement('div');
            toastEl.className = 'toast-lite';
            toastEl.setAttribute('role', 'status');
            toastEl.setAttribute('aria-live', 'polite');
            document.body.appendChild(toastEl);
        }
        toastEl.textContent = message;
        toastEl.classList.add('is-on');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toastEl.classList.remove('is-on'); }, 2400);
    }
    window.mqmToast = toast;

    /* ---------- Flash messages: dismissible, never auto-hidden
       (some carry one-time information such as a generated password) ---------- */
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-dismiss-flash]');
        if (btn) btn.closest('.flash').remove();
    });

    /* ---------- Confirm destructive submits ---------- */
    document.addEventListener('submit', function (e) {
        var form = e.target;
        var msg = form.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    }, true);

    /* ---------- Copy to clipboard ---------- */
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-copy]');
        if (!btn) return;
        var text = btn.getAttribute('data-copy');
        var done = function () { toast(btn.getAttribute('data-copy-done') || 'Copied'); };
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(done, function () { window.prompt('Copy this link:', text); });
        } else {
            window.prompt('Copy this link:', text);
        }
    });

    /* ---------- Dish form: allergens & dietary tags ----------
       Instant feedback only; ProductController enforces the same rules. */
    document.querySelectorAll('[data-dietary]').forEach(function (section) {
        var allergens = section.querySelectorAll('[data-allergen]');
        var none = section.querySelector('[data-allergen-none]');
        var status = section.querySelector('[data-allergen-status]');
        var vegan = section.querySelector('[data-diet="Vegan"]');
        var vegetarian = section.querySelector('[data-diet="Vegetarian"]');
        function anyAllergen() { return Array.prototype.some.call(allergens, function (a) { return a.checked; }); }
        function updateStatus() {
            if (!status) return;
            status.textContent = anyAllergen() || none.checked
                ? 'Declared. Guests see this on the dish and can filter by it.'
                : 'Not declared yet: guests who filter by allergen won\'t see this dish. Tick its allergens, or "Contains none of the 14".';
        }
        section.addEventListener('change', function (e) {
            var t = e.target;
            if (t.hasAttribute('data-allergen') && t.checked) none.checked = false;
            if (t === none && none.checked) allergens.forEach(function (a) { a.checked = false; });
            if (t === vegan && vegan.checked && vegetarian) vegetarian.checked = true;
            if (t === vegetarian && !vegetarian.checked && vegan) vegan.checked = false;
            updateStatus();
        });
    });

    /* ---------- Print page ---------- */
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-print]')) window.print();
    });
    // Option forms update the preview as soon as a choice changes (the form is a plain GET).
    document.querySelectorAll('form[data-autosubmit]').forEach(function (form) {
        form.addEventListener('change', function () {
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
        });
    });

    /* Preview image follows the photo dropzone. Registered before the dropzones
       initialise, because they announce their current image straight away. */
    document.addEventListener('dropzone:change', function (e) {
        var img = document.querySelector('[data-preview-image]');
        var ph = document.querySelector('[data-preview-placeholder]');
        if (!img) return;
        var url = e.detail.url;
        img.hidden = !url;
        if (url) img.src = url;
        if (ph) ph.hidden = !!url;
    });

    /* ---------- Image drop zones ----------
       <label class="dropzone" data-dropzone="inputId" data-fit="contain"> ... </label>
       The file input stays a normal form field; this only adds preview + drag & drop. */
    document.querySelectorAll('[data-dropzone]').forEach(function (zone) {
        var input = document.getElementById(zone.getAttribute('data-dropzone'));
        if (!input) return;
        var original = zone.getAttribute('data-current') || '';
        var clearBtn = zone.querySelector('.dropzone-clear');

        function show(url) {
            zone.style.backgroundImage = url ? 'url("' + url.replace(/"/g, '%22') + '")' : '';
            zone.classList.toggle('has-image', !!url);
            if (clearBtn) clearBtn.hidden = !input.files.length;
            zone.dispatchEvent(new CustomEvent('dropzone:change', { bubbles: true, detail: { url: url } }));
        }
        function fromFile(file) {
            if (!file || !/^image\//.test(file.type)) return;
            var reader = new FileReader();
            reader.onload = function (ev) { show(ev.target.result); };
            reader.readAsDataURL(file);
        }

        if (original) show(original);
        input.addEventListener('change', function () { fromFile(input.files[0]); });

        ['dragenter', 'dragover'].forEach(function (t) {
            zone.addEventListener(t, function (e) { e.preventDefault(); zone.classList.add('is-over'); });
        });
        ['dragleave', 'drop'].forEach(function (t) {
            zone.addEventListener(t, function (e) { e.preventDefault(); zone.classList.remove('is-over'); });
        });
        zone.addEventListener('drop', function (e) {
            var files = e.dataTransfer && e.dataTransfer.files;
            if (!files || !files.length || !/^image\//.test(files[0].type)) return;
            input.files = files;
            fromFile(files[0]);
        });
        zone.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); input.click(); }
        });
        if (clearBtn) {
            clearBtn.hidden = true;
            clearBtn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                input.value = '';
                show(original);
            });
        }
    });

    /* ---------- Live previews ----------
       [data-preview-source="key"] inputs feed [data-preview-target="key"] text. */
    var targets = {};
    document.querySelectorAll('[data-preview-target]').forEach(function (t) {
        targets[t.getAttribute('data-preview-target')] = t;
        t.dataset.fallback = t.textContent;
    });
    document.querySelectorAll('[data-preview-source]').forEach(function (src) {
        var key = src.getAttribute('data-preview-source');
        var target = targets[key];
        if (!target) return;
        var format = src.getAttribute('data-preview-format');
        var update = function () {
            var v = src.value.trim();
            if (format === 'money' && v) {
                var n = parseFloat(v.replace(',', '.'));
                v = isNaN(n) ? '' : (target.getAttribute('data-currency') || '') + n.toFixed(2);
            }
            target.textContent = v || target.dataset.fallback;
            if (target.hasAttribute('data-hide-empty')) target.hidden = !v;
        };
        src.addEventListener('input', update);
        update();
    });
    /* ---------- Translation tabs: tick languages that have content ---------- */
    document.querySelectorAll('[data-lang-pane]').forEach(function (pane) {
        var tab = document.querySelector('[data-bs-target="#' + pane.id + '"] .done');
        if (!tab) return;
        var check = function () {
            var filled = Array.prototype.some.call(pane.querySelectorAll('input, textarea'), function (f) { return f.value.trim(); });
            tab.hidden = !filled;
        };
        pane.addEventListener('input', check);
        check();
    });

    /* ---------- Sold-out switches (Branch Details) ----------
       <form data-availability data-name="…"> holds the antiforgery token, a hidden
       isAvailable field and a role="switch" button. Without JavaScript it is a normal
       post; here it saves in the background. The switch flips at once; if the save
       fails it goes back to the last state the server confirmed. */
    var soldOutCount = document.querySelector('[data-soldout-count]');
    function refreshSoldOutCount() {
        if (!soldOutCount) return;
        var n = document.querySelectorAll('[data-dish].is-soldout').length;
        soldOutCount.textContent = n + ' sold out';
        soldOutCount.hidden = n === 0;
    }
    function showAvailability(form, available) {
        var btn = form.querySelector('[role="switch"]');
        btn.setAttribute('aria-checked', available ? 'true' : 'false');
        btn.title = available ? 'Available. Tap to mark sold out' : 'Sold out. Tap to make available';
        // The no-JavaScript fallback posts this field, so keep it pointing at the next state.
        form.querySelector('input[name="isAvailable"]').value = available ? 'false' : 'true';
        var dish = form.closest('[data-dish]');
        if (dish) {
            dish.classList.toggle('is-soldout', !available);
            var chip = dish.querySelector('[data-soldout-chip]');
            if (chip) chip.hidden = available;
        }
        refreshSoldOutCount();
    }
    document.querySelectorAll('form[data-availability]').forEach(function (form) {
        var btn = form.querySelector('[role="switch"]');
        var name = form.getAttribute('data-name') || 'Dish';
        var confirmed = btn.getAttribute('aria-checked') === 'true';
        var seq = 0; // only the newest request may update the switch

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var want = btn.getAttribute('aria-checked') !== 'true';
            var data = new FormData(form);
            data.set('isAvailable', want ? 'true' : 'false');
            showAvailability(form, want);

            var mine = ++seq;
            btn.setAttribute('aria-busy', 'true');
            fetch(form.action, {
                method: 'POST',
                headers: { 'Accept': 'application/json' },
                body: data,
                credentials: 'same-origin'
            }).then(function (r) {
                // A signed-out session is redirected to the login page, which is not JSON.
                if (!r.ok || r.redirected) throw new Error(r.status);
                return r.json();
            }).then(function (res) {
                confirmed = !!res.isAvailable;
                if (mine !== seq) return;
                showAvailability(form, confirmed);
                toast(confirmed ? name + ' is available again' : name + ' is sold out');
            }).catch(function () {
                if (mine !== seq) return;
                showAvailability(form, confirmed);
                toast('Could not update ' + name + '. Reload the page and try again.');
            }).then(function () {
                if (mine === seq) btn.removeAttribute('aria-busy');
            });
        });
    });

    /* ---------- Drag to reorder categories ---------- */
    // Sortable is loaded by the page's Scripts section, after this file, so wait for load.
    window.addEventListener('load', function () {
    var sortable = document.querySelector('[data-sortable]');
    if (!sortable || !window.Sortable) return;
    {
        var state = document.querySelector('[data-sort-state]');
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        window.Sortable.create(sortable, {
            handle: '.drag-handle',
            animation: 160,
            ghostClass: 'sortable-ghost',
            chosenClass: 'sortable-chosen',
            onEnd: function () {
                var ids = Array.prototype.map.call(sortable.querySelectorAll('[data-category-id]'), function (li) {
                    return parseInt(li.getAttribute('data-category-id'), 10);
                });
                if (state) state.textContent = 'Saving order…';
                fetch(sortable.getAttribute('data-sortable'), {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': tokenInput ? tokenInput.value : ''
                    },
                    body: JSON.stringify({ categoryIds: ids })
                }).then(function (r) {
                    if (!r.ok) throw new Error(r.status);
                    return r.json();
                }).then(function () {
                    if (state) state.textContent = 'Order saved. Guests see it now.';
                    // keep the visible position numbers in sync
                    sortable.querySelectorAll('[data-position]').forEach(function (el, i) { el.textContent = i + 1; });
                }).catch(function () {
                    if (state) state.textContent = 'Could not save the new order. Reload and try again.';
                });
            }
        });
    }
    });
})();
