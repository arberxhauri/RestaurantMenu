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

    /* ---------- Fill translations (dish and category forms) ----------
       <div data-translate data-endpoint data-kind data-branch-id data-languages="sq,de"
            data-sources='{"name":"#Name",...}'>. Sends the English fields, puts the
       suggestions into translation_{field}_{lang} inputs and marks them for review.
       Fills only empty fields unless the owner agrees to replace existing ones. */
    document.querySelectorAll('[data-translate]').forEach(function (bar) {
        var form = bar.closest('form');
        var run = bar.querySelector('[data-translate-run]');
        var label = bar.querySelector('[data-translate-label]');
        var state = bar.querySelector('[data-translate-state]');
        var sources = JSON.parse(bar.getAttribute('data-sources') || '{}');
        var languages = (bar.getAttribute('data-languages') || '').split(',').filter(Boolean);
        var token = form && form.querySelector('input[name="__RequestVerificationToken"]');
        if (!form || !run) return;

        function target(field, lang) {
            if (field.indexOf('opt_') === 0) {
                return form.querySelector('[data-tr-for="' + field.slice(4) + '"][lang="' + lang + '"]');
            }
            return form.querySelector('[name="translation_' + field + '_' + lang + '"]');
        }

        // A suggestion stays highlighted until the owner touches it.
        form.addEventListener('input', function (e) {
            if (e.isTrusted && e.target.classList) e.target.classList.remove('is-suggested');
        });

        run.addEventListener('click', function () {
            var english = {};
            Object.keys(sources).forEach(function (f) {
                var el = form.querySelector(sources[f]);
                english[f] = el ? el.value.trim() : '';
            });
            if (!english.name) {
                state.textContent = 'Write the English name first.';
                var nameEl = form.querySelector(sources.name);
                if (nameEl) nameEl.focus();
                return;
            }

            // Option group and option names from the Sizes & add-ons section.
            form.querySelectorAll('[data-tr-key]').forEach(function (el) {
                var v = el.value.trim();
                if (v) english['opt_' + el.getAttribute('data-tr-key')] = v;
            });
            var fields = Object.keys(english).filter(function (f) { return english[f]; });
            var alreadyFilled = languages.some(function (l) {
                return fields.some(function (f) { var el = target(f, l); return el && el.value.trim(); });
            });
            var replace = alreadyFilled && window.confirm(
                'Some translations are already filled in.\n\nOK: replace them with new suggestions.\nCancel: fill only the empty fields.');
            var wanted = languages.filter(function (l) {
                return fields.some(function (f) { var el = target(f, l); return el && (replace || !el.value.trim()); });
            });
            if (!wanted.length) {
                state.textContent = 'Every translation is already filled in.';
                return;
            }

            run.disabled = true;
            run.setAttribute('aria-busy', 'true');
            label.textContent = 'Translating…';
            state.textContent = 'Translating into ' + wanted.length + (wanted.length === 1 ? ' language…' : ' languages…');

            fetch(bar.getAttribute('data-endpoint'), {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    'RequestVerificationToken': token ? token.value : ''
                },
                body: JSON.stringify({
                    branchId: Number(bar.getAttribute('data-branch-id')),
                    kind: bar.getAttribute('data-kind'),
                    fields: english,
                    languages: wanted
                }),
                credentials: 'same-origin'
            }).then(function (r) {
                if (r.redirected) throw new Error('Your session has ended. Reload the page and sign in again.');
                return r.json().catch(function () { return {}; }).then(function (j) {
                    if (r.status === 429) throw new Error('Too many translation requests. Wait a minute and try again.');
                    if (!r.ok) throw new Error(j.message || 'Translation failed. Try again.');
                    return j;
                });
            }).then(function (j) {
                var count = 0;
                wanted.forEach(function (l) {
                    fields.forEach(function (f) {
                        var el = target(f, l);
                        var value = j.translations && j.translations[l] && j.translations[l][f];
                        if (!el || !value || (!replace && el.value.trim())) return;
                        el.value = value;
                        el.classList.add('is-suggested');
                        el.dispatchEvent(new Event('input', { bubbles: true })); // updates the language tab ticks
                        count++;
                    });
                });
                state.textContent = count
                    ? 'Filled ' + count + (count === 1 ? ' field' : ' fields') + '. Check each language, then save.'
                    : 'No suggestions came back. Try again or translate by hand.';
                // Show the option translations that were just filled in.
                var showTr = form.querySelector('#optShowTr');
                if (showTr && form.querySelector('.opt-tr .is-suggested')) showTr.checked = true;
            }).catch(function (err) {
                state.textContent = err.message || 'Translation failed. Try again.';
            }).then(function () {
                run.disabled = false;
                run.removeAttribute('aria-busy');
                label.textContent = 'Fill translations';
            });
        });
    });

    /* ---------- Opening hours (branch form) ---------- */
    document.querySelectorAll('[data-hours]').forEach(function (section) {
        section.addEventListener('click', function (e) {
            var row = e.target.closest('[data-hours-row]');
            if (e.target.closest('[data-add-second]') && row) {
                row.querySelector('[data-second]').hidden = false;
                row.querySelector('[data-add-second]').hidden = true;
                row.querySelector('[data-second] input').focus();
            }
            if (e.target.closest('[data-remove-second]') && row) {
                row.querySelectorAll('[data-second] input').forEach(function (i) { i.value = ''; });
                row.querySelector('[data-second]').hidden = true;
                row.querySelector('[data-add-second]').hidden = false;
                row.querySelector('[data-add-second]').focus();
            }
            if (e.target.closest('[data-copy-first-day]')) {
                var rows = section.querySelectorAll('[data-hours-row]');
                var first = rows[0];
                var values = Array.prototype.map.call(first.querySelectorAll('input'), function (i) {
                    return i.type === 'checkbox' ? i.checked : i.value;
                });
                Array.prototype.slice.call(rows, 1).forEach(function (r) {
                    r.querySelectorAll('input').forEach(function (i, n) {
                        if (i.type === 'checkbox') i.checked = values[n]; else i.value = values[n];
                    });
                    var hasSecond = !first.querySelector('[data-second]').hidden;
                    r.querySelector('[data-second]').hidden = !hasSecond;
                    r.querySelector('[data-add-second]').hidden = hasSecond;
                });
                toast('Copied Monday to every day');
            }
        });
    });

    /* ---------- Sizes & add-ons repeater (dish form) ----------
       Rows are cloned from <template>s. Field names carry indexes (og-0-o-1-price) that
       are rewritten in DOM order on submit, so adding and removing rows never leaves gaps. */
    document.querySelectorAll('[data-options]').forEach(function (section) {
        var list = section.querySelector('[data-option-groups]');
        var groupTpl = section.querySelector('[data-group-template]');
        var optionTpl = section.querySelector('[data-option-template]');
        var form = section.closest('form');
        var counter = 0;
        function uniqueKey() { counter++; return 'n' + Date.now().toString(36) + counter; }
        function rekey(root, placeholder, key) {
            root.querySelectorAll('[data-tr-key="' + placeholder + '"],[data-tr-for="' + placeholder + '"]').forEach(function (el) {
                if (el.hasAttribute('data-tr-key')) el.setAttribute('data-tr-key', key);
                if (el.hasAttribute('data-tr-for')) el.setAttribute('data-tr-for', key);
            });
        }
        function newOption(name, price) {
            var frag = optionTpl.content.cloneNode(true);
            rekey(frag, '__o__', uniqueKey());
            var row = frag.querySelector('[data-option]');
            if (name) row.querySelector('input[name$="-name"]').value = name;
            if (price) row.querySelector('input[name$="-price"]').value = price;
            return row;
        }
        function addGroup(preset) {
            var frag = groupTpl.content.cloneNode(true);
            var group = frag.querySelector('[data-group]');
            rekey(group, '__g__', uniqueKey());
            var rows = group.querySelector('[data-option-rows]');
            rows.querySelectorAll('[data-option]').forEach(function (r) { r.remove(); });
            var nameInput = group.querySelector('input[name$="-name"]');
            var required = group.querySelector('input[name$="-required"]');
            var max = group.querySelector('input[name$="-max"]');
            if (preset === 'size') {
                nameInput.value = 'Size'; required.checked = true; max.value = '1';
                rows.appendChild(newOption('Small', '')); rows.appendChild(newOption('Large', '1.50'));
            } else if (preset === 'extras') {
                nameInput.value = 'Extras'; required.checked = false; max.value = '3';
                rows.appendChild(newOption('', '')); rows.appendChild(newOption('', ''));
            } else {
                rows.appendChild(newOption('', '')); rows.appendChild(newOption('', ''));
            }
            list.appendChild(group);
            (preset === 'extras' ? rows.querySelector('input') : nameInput).focus();
        }
        section.addEventListener('click', function (e) {
            var add = e.target.closest('[data-add-group]');
            if (add) { addGroup(add.getAttribute('data-add-group')); return; }
            if (e.target.closest('[data-add-option]')) {
                var row = newOption('', '');
                e.target.closest('[data-group]').querySelector('[data-option-rows]').appendChild(row);
                row.querySelector('input').focus();
                return;
            }
            var removeOption = e.target.closest('[data-remove-option]');
            if (removeOption) {
                var group = removeOption.closest('[data-group]');
                removeOption.closest('[data-option]').remove();
                var next = group.querySelector('[data-option] input') || group.querySelector('[data-add-option]');
                next.focus();
                return;
            }
            var removeGroup = e.target.closest('[data-remove-group]');
            if (removeGroup) {
                var g = removeGroup.closest('[data-group]');
                var name = g.querySelector('input').value.trim();
                if (name && !window.confirm('Remove the group "' + name + '" and its options?')) return;
                g.remove();
                section.querySelector('[data-add-group]').focus();
            }
        });
        if (form) form.addEventListener('submit', function () {
            list.querySelectorAll('[data-group]').forEach(function (g, gi) {
                g.querySelectorAll('[name^="og-"]').forEach(function (input) {
                    if (input.closest('[data-option]')) return;
                    input.name = input.name.replace(/^og-\d+-/, 'og-' + gi + '-');
                });
                g.querySelectorAll('[data-option]').forEach(function (o, oi) {
                    o.querySelectorAll('[name^="og-"]').forEach(function (input) {
                        input.name = input.name.replace(/^og-\d+-o-\d+-/, 'og-' + gi + '-o-' + oi + '-');
                    });
                });
            });
        });
    });

    /* ---------- Brand (branch form): pickers, contrast, live preview ----------
       Same contrast rules as Helpers/BrandTheme.cs, so the preview shows what guests get:
       text on a colour is black or white, coloured text is shaded until it reads at 4.5:1. */
    var brandSection = document.querySelector('[data-brand]');
    if (brandSection) (function () {
        var LIGHT_BG = '#F4F4F2', DARK_BG = '#1C1C1F', DARK_TEXT = '#141414';
        function norm(v) {
            var m = /^#?([0-9a-f]{6}|[0-9a-f]{3})$/i.exec((v || '').trim());
            if (!m) return null;
            var h = m[1].length === 3 ? m[1].replace(/(.)/g, '$1$1') : m[1];
            return '#' + h.toUpperCase();
        }
        function rgb(h) { return [1, 3, 5].map(function (i) { return parseInt(h.substr(i, 2), 16); }); }
        function lum(h) {
            var c = rgb(h).map(function (v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
            return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
        }
        function contrast(a, b) { var x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); }
        function textOn(bg) {
            var w = contrast('#FFFFFF', bg), d = contrast(DARK_TEXT, bg);
            if (Math.max(w, d) >= 4.5) return w >= d ? '#FFFFFF' : DARK_TEXT;
            return '#000000';
        }
        function textOn2(a, b) {
            var w = Math.min(contrast('#FFFFFF', a), contrast('#FFFFFF', b));
            var d = Math.min(contrast(DARK_TEXT, a), contrast(DARK_TEXT, b));
            if (Math.max(w, d) >= 4.5 || w >= d) return w >= d ? '#FFFFFF' : DARK_TEXT;
            return '#000000';
        }
        function hex(r, g, b) { return '#' + [r, g, b].map(function (v) { return ('0' + Math.round(v).toString(16)).slice(-2); }).join('').toUpperCase(); }
        function readable(c, bg) {
            if (contrast(c, bg) >= 4.5) return c;
            var to = lum(bg) > 0.5 ? 0 : 255, p = rgb(c);
            for (var t = 0.04; t <= 1.0001; t += 0.04) {
                var m = hex(p[0] + (to - p[0]) * t, p[1] + (to - p[1]) * t, p[2] + (to - p[2]) * t);
                if (contrast(m, bg) >= 4.5) return m;
            }
            return lum(bg) > 0.5 ? DARK_TEXT : '#FFFFFF';
        }

        // The preview sits in the form's side column, outside the Brand section.
        var preview = document.querySelector('[data-brand-preview]');
        var modeButtons = document.querySelectorAll('[data-preview-mode]');
        var notesEl = brandSection.querySelector('[data-brand-notes]');
        var hasBanner = !!preview.getAttribute('data-banner');
        var previewMode = 'light';
        var keys = ['primary', 'secondary', 'accent'];
        function field(k) { return brandSection.querySelector('[data-brand-hex="' + k + '"]'); }
        function picker(k) { return brandSection.querySelector('[data-brand-picker="' + k + '"]'); }
        function current(k) { return norm(field(k).value) || norm(picker(k).value); }
        function checked(name) { var el = brandSection.querySelector('input[name="' + name + '"]:checked'); return el ? el.value : ''; }

        function update() {
            var p = current('primary'), s2 = current('secondary'), a = current('accent');
            var header = checked('brand_header');
            if (header === 'photo' && !hasBanner) header = 'colour';
            var appearance = checked('brand_appearance');
            var mode = appearance === 'auto' ? previewMode : appearance;
            var ink = mode === 'dark' ? DARK_BG : LIGHT_BG;
            preview.setAttribute('data-header', header);
            preview.setAttribute('data-mode', mode);
            var st = preview.style;
            st.setProperty('--bp-brand', p); st.setProperty('--bp-brand-2', s2); st.setProperty('--bp-brand-3', a);
            st.setProperty('--bp-on-brand', textOn(p));
            st.setProperty('--bp-hero-ink', header === 'minimal' ? 'var(--bp-ink)' : textOn2(p, s2));
            st.setProperty('--bp-price', readable(p, ink));
            st.setProperty('--bp-badge-ink', readable(a, ink));
            modeButtons.forEach(function (b) {
                b.setAttribute('aria-pressed', b.getAttribute('data-preview-mode') === mode ? 'true' : 'false');
                b.disabled = appearance !== 'auto' && b.getAttribute('data-preview-mode') !== appearance;
            });
            var notes = [], light = appearance !== 'dark', dark = appearance !== 'light';
            if (light && contrast(p, LIGHT_BG) < 3) notes.push('The main colour is very light, so prices on a light menu use a darker shade of it.');
            if (dark && contrast(p, DARK_BG) < 3) notes.push('The main colour is very dark, so prices on a dark menu use a lighter shade of it.');
            if ((light && contrast(a, LIGHT_BG) < 3) || (dark && contrast(a, DARK_BG) < 3)) notes.push('Badge text uses an adjusted shade of the highlight colour so it stays readable.');
            notesEl.textContent = '';
            notes.forEach(function (n) { var li = document.createElement('li'); li.textContent = n; notesEl.appendChild(li); });
        }

        keys.forEach(function (k) {
            picker(k).addEventListener('input', function () {
                field(k).value = picker(k).value.toUpperCase();
                field(k).classList.remove('is-invalid');
                update();
            });
            field(k).addEventListener('input', function () {
                var v = norm(field(k).value);
                field(k).classList.toggle('is-invalid', !v);
                if (v) { picker(k).value = v.toLowerCase(); update(); }
            });
            field(k).addEventListener('blur', function () {
                var v = norm(field(k).value);
                if (v) field(k).value = v;
            });
        });
        brandSection.addEventListener('change', function (e) {
            if (e.target.name === 'brand_header' || e.target.name === 'brand_appearance') update();
        });
        modeButtons.forEach(function (b) {
            b.addEventListener('click', function () { previewMode = b.getAttribute('data-preview-mode'); update(); });
        });
        var reset = brandSection.querySelector('[data-brand-reset]');
        if (reset) reset.addEventListener('click', function () {
            keys.forEach(function (k) {
                var logo = norm(field(k).getAttribute('data-logo'));
                if (logo) { field(k).value = logo; picker(k).value = logo.toLowerCase(); field(k).classList.remove('is-invalid'); }
            });
            update();
            toast('Colours from your logo');
        });
        var nameInput = document.getElementById('Name');
        var nameEl = preview.querySelector('[data-bp-name]');
        var initialEl = preview.querySelector('[data-bp-initial]');
        if (nameInput) nameInput.addEventListener('input', function () {
            var v = nameInput.value.trim();
            nameEl.textContent = v || 'Your restaurant';
            if (initialEl) initialEl.textContent = (v || 'R').charAt(0);
        });
        update();
    })();

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

    /* ---------- Recommend (star) on Branch Details ----------
       Same pattern as the sold-out switch: flips at once, saves in the background,
       goes back to the last confirmed state if saving fails. */
    document.querySelectorAll('form[data-featured]').forEach(function (form) {
        var btn = form.querySelector('button');
        var icon = btn.querySelector('i');
        var name = form.getAttribute('data-name') || 'Dish';
        var confirmed = btn.getAttribute('aria-pressed') === 'true';
        var seq = 0;
        function show(on) {
            btn.setAttribute('aria-pressed', on ? 'true' : 'false');
            icon.className = (on ? 'ph-fill' : 'ph') + ' ph-star';
            btn.title = on ? 'Recommended. Tap to remove' : 'Recommend at the top of the menu';
            form.querySelector('input[name="isFeatured"]').value = on ? 'false' : 'true';
        }
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var want = btn.getAttribute('aria-pressed') !== 'true';
            var data = new FormData(form);
            data.set('isFeatured', want ? 'true' : 'false');
            show(want);
            var mine = ++seq;
            btn.setAttribute('aria-busy', 'true');
            fetch(form.action, { method: 'POST', headers: { 'Accept': 'application/json' }, body: data, credentials: 'same-origin' })
                .then(function (r) {
                    if (!r.ok || r.redirected) throw new Error(r.status);
                    return r.json();
                }).then(function (res) {
                    confirmed = !!res.isFeatured;
                    if (mine !== seq) return;
                    show(confirmed);
                    toast(confirmed ? name + ' is recommended at the top of the menu' : name + ' is no longer recommended');
                }).catch(function () {
                    if (mine !== seq) return;
                    show(confirmed);
                    toast('Could not update ' + name + '. Reload the page and try again.');
                }).then(function () {
                    if (mine === seq) btn.removeAttribute('aria-busy');
                });
        });
    });

    /* ---------- Drag to reorder (categories and dishes) ----------
       <ul|div data-sortable="endpoint" data-sortable-item="data-category-id"
               data-sortable-key="categoryIds" [data-sortable-extra='{"categoryId":5}']
               [data-sortable-state="selector for a status line; otherwise a toast"]>
       Children with the item attribute move by their .drag-handle: dragged (SortableJS),
       or with ArrowUp / ArrowDown while the handle has focus. The order is posted as
       { <key>: [ids], ...extra }. If saving fails the list returns to the last saved order. */
    var sortToken = document.querySelector('input[name="__RequestVerificationToken"]');

    function initSortable(list) {
        var endpoint = list.getAttribute('data-sortable');
        if (!endpoint) return; // read-only for this person (Razor renders a null data-* as "")
        var itemAttr = list.getAttribute('data-sortable-item');
        var key = list.getAttribute('data-sortable-key');
        var extra = {};
        try { extra = JSON.parse(list.getAttribute('data-sortable-extra') || '{}'); } catch (e) { /* none */ }
        var stateSel = list.getAttribute('data-sortable-state');
        var state = stateSel ? document.querySelector(stateSel) : null;

        function items() {
            return Array.prototype.filter.call(list.children, function (el) { return el.hasAttribute(itemAttr); });
        }
        function ids() { return items().map(function (el) { return parseInt(el.getAttribute(itemAttr), 10); }); }
        function report(message) { if (state) state.textContent = message; else toast(message); }
        function renumber() {
            list.querySelectorAll('[data-position]').forEach(function (el, i) { el.textContent = i + 1; });
        }
        function restore(order) {
            var byId = {};
            items().forEach(function (el) { byId[el.getAttribute(itemAttr)] = el; });
            // Re-appending in order keeps any non-item children (a group title) in front.
            order.forEach(function (id) { if (byId[id]) list.appendChild(byId[id]); });
            renumber();
        }

        var saved = ids();      // order the server has
        var seq = 0;            // newest request; older responses are ignored
        var savedSeq = 0;
        var timer = null;

        function save(delay) {
            renumber();
            clearTimeout(timer);
            timer = setTimeout(function () {
                var order = ids();
                if (order.join() === saved.join()) return;
                var mine = ++seq;
                if (state) state.textContent = 'Saving order…';
                var body = {};
                Object.keys(extra).forEach(function (k) { body[k] = extra[k]; });
                body[key] = order;
                fetch(endpoint, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'RequestVerificationToken': sortToken ? sortToken.value : ''
                    },
                    body: JSON.stringify(body),
                    credentials: 'same-origin'
                }).then(function (r) {
                    if (r.status === 409) {
                        return r.json().catch(function () { return {}; }).then(function (j) {
                            throw new Error(j.message || 'This list changed somewhere else. Reload the page.');
                        });
                    }
                    // A signed-out session is redirected to the login page, which is not JSON.
                    if (!r.ok || r.redirected) throw new Error('');
                    return r.json();
                }).then(function () {
                    if (mine > savedSeq) { saved = order; savedSeq = mine; }
                    if (mine === seq) report('Order saved. Guests see it now.');
                }).catch(function (err) {
                    if (mine !== seq) return;
                    restore(saved);
                    report(err.message || 'Could not save the new order. Reload the page and try again.');
                });
            }, delay);
        }

        list.addEventListener('keydown', function (e) {
            if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
            var handle = e.target.closest('.drag-handle');
            var item = handle && handle.closest('[' + itemAttr + ']');
            if (!item || item.parentNode !== list) return;
            var all = items();
            var from = all.indexOf(item);
            var to = e.key === 'ArrowUp' ? from - 1 : from + 1;
            e.preventDefault();
            if (to < 0 || to >= all.length) return;
            list.insertBefore(item, e.key === 'ArrowUp' ? all[to] : all[to].nextSibling);
            handle.focus();
            report((item.getAttribute('data-sort-name') || 'Item') + ' moved to position ' + (to + 1) + ' of ' + all.length);
            save(700); // several key presses in a row become one save
        });

        if (window.Sortable) {
            window.Sortable.create(list, {
                handle: '.drag-handle',
                draggable: '[' + itemAttr + ']',
                animation: 160,
                ghostClass: 'sortable-ghost',
                chosenClass: 'sortable-chosen',
                onEnd: function () { save(0); }
            });
        }
    }

    // Sortable is loaded by the page's Scripts section, after this file, so wait for load.
    window.addEventListener('load', function () {
        document.querySelectorAll('[data-sortable]').forEach(initSortable);
    });
})();
