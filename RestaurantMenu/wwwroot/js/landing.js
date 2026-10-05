/* Landing page behaviour.
   1. Mobile nav + booking-slot demo: always on.
   2. Scroll choreography (GSAP + ScrollTrigger + Lenis): only when the visitor
      allows motion and the libraries loaded. Otherwise the page stays the
      static layout described in landing.css. */
(function () {
    'use strict';

    var root = document.documentElement;
    var header = document.querySelector('[data-header]');

    /* ---------- Mobile navigation ---------- */
    var toggle = document.querySelector('[data-nav-toggle]');
    var nav = document.getElementById('primaryNav');
    function setNav(open) {
        nav.classList.toggle('is-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
        toggle.innerHTML = open
            ? '<i class="ph ph-x" aria-hidden="true"></i>'
            : '<i class="ph ph-list" aria-hidden="true"></i>';
        header.classList.toggle('is-solid', open || header.dataset.solid === 'true');
    }
    if (toggle && nav) {
        toggle.addEventListener('click', function () { setNav(!nav.classList.contains('is-open')); });
        nav.addEventListener('click', function (e) { if (e.target.closest('a')) setNav(false); });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && nav.classList.contains('is-open')) { setNav(false); toggle.focus(); }
        });
    }

    /* ---------- Booking slots demo (real buttons, real feedback) ---------- */
    var slotGroup = document.querySelector('.slots');
    var slotNote = document.querySelector('[data-slot-note]');
    if (slotGroup) {
        slotGroup.addEventListener('click', function (e) {
            var btn = e.target.closest('.slot');
            if (!btn || btn.disabled) return;
            slotGroup.querySelectorAll('.slot[aria-pressed]').forEach(function (b) {
                b.setAttribute('aria-pressed', b === btn ? 'true' : 'false');
            });
            if (slotNote) slotNote.textContent = 'Table for 4 held at ' + btn.textContent.trim() + '. Confirmation goes out by SMS or email.';
        });
    }

    /* ---------- Header: solid once we leave the photo ---------- */
    function setSolid(solid) {
        header.dataset.solid = solid ? 'true' : 'false';
        header.classList.toggle('is-solid', solid || (nav && nav.classList.contains('is-open')));
    }

    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var hasGsap = window.gsap && window.ScrollTrigger;

    if (reduceMotion || !hasGsap) {
        // Static page: the header scrolls away with the hero, nothing to choreograph.
        return;
    }

    var gsap = window.gsap;
    var ScrollTrigger = window.ScrollTrigger;
    gsap.registerPlugin(ScrollTrigger);
    root.classList.add('motion-ok');

    /* ---------- Smooth scroll ---------- */
    var lenis = null;
    if (window.Lenis) {
        lenis = new window.Lenis({ lerp: 0.11, wheelMultiplier: 1, smoothWheel: true });
        lenis.on('scroll', ScrollTrigger.update);
        gsap.ticker.add(function (time) { lenis.raf(time * 1000); });
        gsap.ticker.lagSmoothing(0);
    }

    // In-page anchors go through Lenis so they respect the pinned hero's spacer.
    document.addEventListener('click', function (e) {
        var link = e.target.closest('a[href^="#"]');
        if (!link) return;
        var id = link.getAttribute('href');
        if (id.length < 2) return;
        var target = document.querySelector(id);
        if (!target) return;
        e.preventDefault();
        var offset = -(header ? header.offsetHeight : 68);
        if (lenis) lenis.scrollTo(target, { offset: offset, duration: 1.2 });
        else target.scrollIntoView({ behavior: 'smooth' });
        history.replaceState(null, '', id);
    });

    /* ---------- Hero: table -> plate -> phone -> live menu ---------- */
    var stage = document.querySelector('[data-hero-stage]');
    var media = document.querySelector('[data-hero-media]');
    var table = document.querySelector('[data-hero-table]');
    var plate = document.querySelector('[data-hero-plate]');
    var scrim = document.querySelector('[data-hero-scrim]');
    var copy = document.querySelector('[data-hero-copy]');
    var device = document.querySelector('[data-device]');
    var ui = document.querySelector('[data-device-ui]');
    var frame = device ? device.querySelector('.device-frame') : null;
    var storyText = document.querySelector('[data-story-text]');
    var callouts = gsap.utils.toArray('[data-callout]');

    // Geometry is recomputed on every ScrollTrigger refresh (resize, font load).
    var g = {};
    function measure() {
        var vw = stage.clientWidth;
        var vh = stage.clientHeight;
        var mobile = vw < 900;
        var D = mobile ? Math.min(vw * 0.7, vh * 0.42) : Math.min(vw * 0.5, vh * 0.58);
        var sw = mobile ? Math.min(vw * 0.62, (vh * 0.56) / 2.05) : Math.min(300, (vh * 0.74) / 2.05);
        var sh = sw * 2.05;
        g = {
            vw: vw, vh: vh, D: D, sw: sw, sh: sh,
            plate: D * 1.04, // the bowl fills ~96% of the square photo
            mobile: mobile
        };
        stage.style.setProperty('--sw', sw + 'px');
        stage.style.setProperty('--sh', sh + 'px');
        stage.style.setProperty('--plate', g.plate + 'px');
        stage.style.setProperty('--u', (sw / 290).toFixed(4)); // phone UI scale, unitless
    }
    // The clip is tweened as plain numbers and written out by hand. Tweening the
    // clip-path string directly breaks: browsers report it shortened
    // ("inset(189px 459px round 261px)"), so GSAP would pair up the wrong numbers.
    var clip = { x: 0, y: 0, r: 0 };
    function applyClip() {
        media.style.clipPath = 'inset(' + clip.y + 'px ' + clip.x + 'px ' + clip.y + 'px ' + clip.x + 'px round ' + clip.r + 'px)';
    }
    function clipTo(w, h, r) {
        return {
            x: function () { return (g.vw - w()) / 2; },
            y: function () { return (g.vh - h()) / 2; },
            r: function () { return r(); }
        };
    }

    measure();
    ScrollTrigger.addEventListener('refreshInit', measure);

    var mm = gsap.matchMedia();
    mm.add({ wide: '(min-width: 900px)', narrow: '(max-width: 899px)' }, function (ctx) {
        var dim = ctx.conditions.wide ? 0.34 : 0; // inactive callouts: dimmed on desktop, hidden on phones

        clip.x = clip.y = clip.r = 0;
        applyClip();
        gsap.set(plate, { opacity: 0, scale: 1.35 });
        gsap.set(ui, { yPercent: 108 });
        gsap.set(frame, { opacity: 0, scale: 1.12 });
        gsap.set(storyText, { opacity: 0, y: 24 });
        gsap.set(callouts, { opacity: 0, y: 16 });

        var tl = gsap.timeline({
            defaults: { ease: 'none' },
            scrollTrigger: {
                trigger: stage,
                start: 'top top',
                end: function () { return '+=' + Math.round(g.vh * 3.4); },
                pin: true,
                scrub: 0.9,
                invalidateOnRefresh: true,
                onUpdate: function (self) { setSolid(self.progress > 0.04); },
                onLeave: function () { setSolid(true); },
                onEnterBack: function () { setSolid(true); }
            }
        });

        // Beat 1: the headline lifts away, the photo starts to close in.
        tl.to(copy, { opacity: 0, y: -80, duration: 1, ease: 'power1.in' }, 0)
          .to(scrim, { opacity: 0, duration: 1.6 }, 0.4)
          .to(clip, Object.assign(clipTo(function () { return g.D; }, function () { return g.D; }, function () { return g.D / 2; }),
              { duration: 3, ease: 'power2.inOut', onUpdate: applyClip }), 0)
          .to(table, { scale: 1.18, duration: 3, ease: 'power1.inOut' }, 0)
          // Beat 2: the shared table resolves into one plate (crossfade once the crop is nearly round).
          .to(table, { opacity: 0, duration: 0.9 }, 1.9)
          .to(plate, { opacity: 1, duration: 0.9 }, 1.9)
          .to(plate, { scale: 1, duration: 1.5, ease: 'power2.out' }, 1.9)
          .to(plate, { rotation: 14, duration: 1.5, ease: 'sine.inOut' }, 1.9)
          // Beat 3: the plate becomes the banner of a phone menu. The plate turns back
          // square-on before the crop widens so the photo's corners never show.
          .to(plate, { rotation: 0, duration: 0.7, ease: 'power2.inOut' }, 3.4)
          .to(clip, Object.assign(clipTo(function () { return g.sw; }, function () { return g.sh; }, function () { return g.mobile ? 30 : 38; }),
              { duration: 2.2, ease: 'power3.inOut', onUpdate: applyClip }), 3.6)
          .to(plate, {
              scale: function () { return Math.max((g.sw * 1.2) / g.D, (g.sw * 1.08) / g.plate); },
              y: function () { return -0.3 * g.sh; },
              duration: 2.2,
              ease: 'power3.inOut'
          }, 3.6)
          .to(ui, { yPercent: 0, duration: 1.8, ease: 'power3.out' }, 4.2)
          .to(frame, { opacity: 1, scale: 1, duration: 1.2, ease: 'power2.out' }, 4.9)
          .to(storyText, { opacity: 1, y: 0, duration: 1 }, 5.6);

        // Beat 4: the menu demonstrates three live edits, one per callout.
        var t = 6.6;
        var step = 1.5;
        callouts.forEach(function (c, i) {
            var at = t + i * step;
            tl.to(c, { opacity: 1, y: 0, duration: 0.6 }, at);
            if (i > 0) tl.to(callouts[i - 1], { opacity: dim, duration: 0.6 }, at);
        });
        // price change
        tl.to('[data-price-a]', { opacity: 0, yPercent: -60, duration: 0.5 }, t + 0.3)
          .fromTo('[data-price-b]', { opacity: 0, yPercent: 60 }, { opacity: 1, yPercent: 0, duration: 0.5 }, t + 0.3)
        // language flip
          .to('[data-lang-a], [data-tr-a]', { opacity: 0, duration: 0.5, stagger: 0.05 }, t + step + 0.3)
          .to('[data-lang-b], [data-tr-b]', { opacity: 1, duration: 0.5, stagger: 0.05 }, t + step + 0.3)
        // sold out
          .to('[data-soldout] > :not([data-soldout-tag])', { opacity: 0.35, duration: 0.5 }, t + step * 2 + 0.3)
          .to('[data-soldout-tag]', { opacity: 1, duration: 0.5 }, t + step * 2 + 0.3)
        // hold the final frame for a moment before the page moves on
          .to({}, { duration: 1.2 });

        return function () {
            setSolid(false);
            media.style.clipPath = '';
        };
    });

    /* ---------- Section reveals ---------- */
    gsap.set('[data-reveal]', { opacity: 0, y: 28 });
    ScrollTrigger.batch('[data-reveal]', {
        start: 'top 88%',
        once: true,
        onEnter: function (els) {
            gsap.to(els, { opacity: 1, y: 0, duration: 0.9, ease: 'power3.out', stagger: 0.08, overwrite: true });
        }
    });

    /* ---------- Steps: each card settles back as the next one arrives ---------- */
    var cards = gsap.utils.toArray('[data-stack-card] .stack-inner');
    cards.forEach(function (card, i) {
        var next = cards[i + 1];
        if (!next) return;
        gsap.to(card, {
            scale: 0.93,
            opacity: 0.6,
            ease: 'none',
            scrollTrigger: {
                trigger: next.parentElement,
                start: 'top bottom',
                end: 'top ' + (header ? header.offsetHeight + 40 : 108) + 'px',
                scrub: true
            }
        });
    });

    /* ---------- Closing CTA: slow push-in on the photo ---------- */
    var ctaPhoto = document.querySelector('.cta-photo');
    if (ctaPhoto) {
        gsap.fromTo(ctaPhoto, { scale: 1.15 }, {
            scale: 1,
            ease: 'none',
            scrollTrigger: { trigger: '.cta', start: 'top bottom', end: 'bottom bottom', scrub: true }
        });
    }

    // Fonts change line lengths, which changes the pin spacer. Re-measure once they land.
    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(function () { ScrollTrigger.refresh(); });
    }
})();
