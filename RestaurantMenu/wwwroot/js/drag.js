/* Drag and drop with pointer events, for mouse, pen and touch alike (HTML5 drag and drop doesn't work
   on most tablets). Used by the kitchen and bar screens (dishes and cards between columns) and the floor
   editor (tables on the grid).

   A mouse or pen drags once it moves a few pixels. A finger drags after a short press and hold, so a
   quick swipe still scrolls the page. While dragging, a copy of the element follows the pointer and
   the drop zone under it is marked; letting go calls drop with the zone (or null outside every zone).
   Buttons, links and form fields inside a draggable element keep working as usual.

   MqmDrag.attach(root, {
       handle: '[data-drag]',           // what can be dragged (inside root)
       zones: '[data-drop]',            // where it can be dropped (anywhere on the page)
       start(el) -> false to refuse,    // optional
       drop(el, zone, point, offset),   // point: {x, y} in the viewport; offset: pointer to the element's top-left
   }) */
(function () {
    'use strict';

    var MOVE_PX = 8;          // a mouse moves this far before a drag starts
    var HOLD_MS = 280;        // a finger holds this long before a drag starts
    var INTERACTIVE = 'button, a, input, select, textarea, label, [contenteditable]';

    var dragging = false;
    // Once a finger drag has started, the page must not scroll under it.
    document.addEventListener('touchmove', function (e) { if (dragging) e.preventDefault(); }, { passive: false });

    function attach(root, opts) {
        var state = null;

        function zoneAt(x, y) {
            var hit = document.elementFromPoint(x, y);
            return hit ? hit.closest(opts.zones) : null;
        }

        function mark(zone) {
            if (state.zone === zone) return;
            if (state.zone) state.zone.classList.remove('is-drop-target');
            state.zone = zone;
            if (zone) zone.classList.add('is-drop-target');
        }

        function begin() {
            var rect = state.el.getBoundingClientRect();
            var ghost = state.el.cloneNode(true);
            ghost.classList.add('drag-ghost');
            ghost.removeAttribute('id');
            ghost.setAttribute('aria-hidden', 'true');
            ghost.style.width = rect.width + 'px';
            ghost.style.height = rect.height + 'px';
            document.body.appendChild(ghost);
            state.ghost = ghost;
            state.offset = { x: state.x - rect.left, y: state.y - rect.top };
            state.el.classList.add('is-dragging');
            state.active = true;
            dragging = true;
            if (navigator.vibrate) { try { navigator.vibrate(10); } catch (e) { /* not allowed */ } }
            follow(state.x, state.y);
        }

        function follow(x, y) {
            state.ghost.style.transform = 'translate(' + (x - state.offset.x) + 'px,' + (y - state.offset.y) + 'px)';
            mark(zoneAt(x, y));
        }

        function end(commit) {
            if (!state) return;
            clearTimeout(state.timer);
            var s = state;
            state = null;
            dragging = false;
            if (!s.active) return;
            if (s.ghost) s.ghost.remove();
            s.el.classList.remove('is-dragging');
            if (s.zone) s.zone.classList.remove('is-drop-target');
            // The click that ends a drag must not also press a button under the pointer.
            var swallow = function (e) { e.stopPropagation(); e.preventDefault(); };
            window.addEventListener('click', swallow, { capture: true, once: true });
            setTimeout(function () { window.removeEventListener('click', swallow, { capture: true }); }, 0);
            if (commit) opts.drop(s.el, s.zone, { x: s.lastX, y: s.lastY }, s.offset);
        }

        root.addEventListener('pointerdown', function (e) {
            if (state || (e.pointerType === 'mouse' && e.button !== 0)) return;
            var el = e.target.closest(opts.handle);
            if (!el || !root.contains(el)) return;
            var interactive = e.target.closest(INTERACTIVE);
            if (interactive && interactive !== el && el.contains(interactive)) return;
            if (opts.start && opts.start(el) === false) return;

            state = { el: el, x: e.clientX, y: e.clientY, lastX: e.clientX, lastY: e.clientY, id: e.pointerId,
                touch: e.pointerType === 'touch', active: false, zone: null };
            if (state.touch) {
                state.timer = setTimeout(function () { if (state && !state.active) begin(); }, HOLD_MS);
            }
        });

        window.addEventListener('pointermove', function (e) {
            if (!state || e.pointerId !== state.id) return;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
            var moved = Math.abs(e.clientX - state.x) > MOVE_PX || Math.abs(e.clientY - state.y) > MOVE_PX;
            if (!state.active) {
                if (state.touch) { if (moved) end(false); } // a swipe: let the page scroll
                else if (moved) begin();
                return;
            }
            e.preventDefault();
            follow(e.clientX, e.clientY);
        });

        window.addEventListener('pointerup', function (e) { if (state && e.pointerId === state.id) end(true); });
        window.addEventListener('pointercancel', function (e) { if (state && e.pointerId === state.id) end(false); });
        window.addEventListener('keydown', function (e) { if (state && e.key === 'Escape') end(false); });
        // A held finger shouldn't open the browser's menu.
        root.addEventListener('contextmenu', function (e) { if (state && state.touch) e.preventDefault(); });
    }

    window.MqmDrag = { attach: attach };
})();
