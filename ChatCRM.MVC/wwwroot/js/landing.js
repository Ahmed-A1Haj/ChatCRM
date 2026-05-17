(function () {
    'use strict';

    const body = document.body;

    // Sticky nav background shift on scroll
    const nav = document.getElementById('pzNav');
    if (nav) {
        const onScroll = () => {
            if (window.scrollY > 8) nav.classList.add('is-scrolled');
            else nav.classList.remove('is-scrolled');
        };
        document.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
    }

    // Mobile menu toggle + body scroll lock
    const toggle = document.getElementById('pzNavToggle');
    const menu = document.getElementById('pzNavMenu');
    if (toggle && menu) {
        const setOpen = (open) => {
            menu.classList.toggle('is-open', open);
            body.classList.toggle('is-locked', open);
            toggle.setAttribute('aria-expanded', String(open));
        };
        toggle.addEventListener('click', () => {
            setOpen(!menu.classList.contains('is-open'));
        });
        menu.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', () => setOpen(false));
        });
        // Close menu if viewport grows past mobile breakpoint
        window.addEventListener('resize', () => {
            if (window.innerWidth > 768 && menu.classList.contains('is-open')) {
                setOpen(false);
            }
        });
    }

    // Reveal-on-scroll using IntersectionObserver — falls back to immediate
    // visibility on browsers without IO support so nothing is hidden.
    const revealEls = document.querySelectorAll('.pz-reveal');
    if ('IntersectionObserver' in window) {
        const io = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-visible');
                    io.unobserve(entry.target);
                }
            });
        }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
        revealEls.forEach(el => io.observe(el));
    } else {
        revealEls.forEach(el => el.classList.add('is-visible'));
    }

    // Smooth-scroll anchor links account for sticky nav offset
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', e => {
            const id = anchor.getAttribute('href');
            if (!id || id === '#' || id.length < 2) return;
            const target = document.querySelector(id);
            if (!target) return;
            e.preventDefault();
            const offset = (nav ? nav.offsetHeight : 0) + 12;
            const top = target.getBoundingClientRect().top + window.scrollY - offset;
            window.scrollTo({ top, behavior: 'smooth' });
        });
    });
})();
