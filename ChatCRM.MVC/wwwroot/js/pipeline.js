/* Kanban pipeline board. Columns = lifecycle stages; dragging a card between columns
   changes the contact's lifecycle stage via the existing /api/contacts/{id}/lifecycle
   endpoint. Pure vanilla JS — native HTML5 drag-and-drop + transform-based animations,
   no external library. */
(function () {
    'use strict';

    // Lifecycle stages → columns. Mirrors STAGES in contacts.js (same i18n keys + lc-* colors).
    const STAGES = [
        { id: 0,  key: 'Lifecycle.NewClient',          cls: 'gray'    },
        { id: 1,  key: 'Lifecycle.NotResponding',      cls: 'red'     },
        { id: 2,  key: 'Lifecycle.Interested',         cls: 'blue'    },
        { id: 3,  key: 'Lifecycle.Thinking',           cls: 'yellow'  },
        { id: 4,  key: 'Lifecycle.WantsAMeeting',      cls: 'purple'  },
        { id: 5,  key: 'Lifecycle.WaitingForMeeting',  cls: 'orange'  },
        { id: 6,  key: 'Lifecycle.Discussed',          cls: 'teal'    },
        { id: 7,  key: 'Lifecycle.PotentialClient',    cls: 'indigo'  },
        { id: 8,  key: 'Lifecycle.WillMakePayment',    cls: 'green'   },
        { id: 9,  key: 'Lifecycle.WaitingForContract', cls: 'amber'   },
        { id: 10, key: 'Lifecycle.OurClient',          cls: 'emerald' }
    ];
    const WON_STAGE = 10; // OurClient — the celebration column.

    const CHANNELS = {
        0: { label: 'WhatsApp',  svg: '<path d="M17.6 6.3A7.85 7.85 0 0 0 12 4a7.94 7.94 0 0 0-6.9 11.9L4 20l4.2-1.1A7.93 7.93 0 0 0 12 20a7.94 7.94 0 0 0 5.6-13.7zM12 18.6a6.6 6.6 0 0 1-3.4-.9l-.24-.15-2.5.66.67-2.43-.16-.25A6.59 6.59 0 1 1 12 18.6z"/>' }
    };

    const state = {
        cards: [],                 // flat list of all loaded cards (each has .lifecycleStage)
        filters: { mine: false, week: false, unassigned: false },
        search: '',
        columnEls: new Map()       // stage -> { listEl, countEl }
    };

    const board = document.getElementById('plBoard');
    const loading = document.getElementById('plLoading');
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // ── Helpers ──────────────────────────────────────────────────────────
    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function relativeTime(iso) {
        if (!iso) return t('Pipeline.NoContact');
        const then = new Date(iso).getTime();
        const diff = Math.max(0, Date.now() - then);
        const m = Math.floor(diff / 60000), h = Math.floor(m / 60), d = Math.floor(h / 24);
        if (d > 0) return t('Pipeline.DaysAgo', d);
        if (h > 0) return t('Pipeline.HoursAgo', h);
        if (m > 0) return t('Pipeline.MinAgo', m);
        return t('Pipeline.JustNow');
    }

    function stageMeta(id) { return STAGES[id] || STAGES[0]; }

    function showToast(message, type) {
        const stack = document.getElementById('toastStack');
        if (!stack) return;
        const node = document.createElement('div');
        node.className = 'toast toast-' + (type || 'info');
        node.textContent = message;
        stack.appendChild(node);
        setTimeout(() => { node.classList.add('toast-out'); setTimeout(() => node.remove(), 200); }, 2600);
    }

    // ── Data load ────────────────────────────────────────────────────────
    async function loadBoard() {
        loading.classList.remove('d-none');
        try {
            const res = await fetch('/api/contacts/board', { headers: { Accept: 'application/json' } });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const data = await res.json();
            // Flatten columns → cards (each card already carries its lifecycleStage).
            state.cards = (data.columns || []).flatMap(col => col.cards || []);
            renderBoard();
        } catch (e) {
            board.innerHTML = `<div class="pl-board-error">${escapeHtml(t('Pipeline.LoadError'))}</div>`;
        } finally {
            loading.classList.add('d-none');
        }
    }

    // ── Filtering ────────────────────────────────────────────────────────
    function cardPassesFilters(c) {
        const f = state.filters;
        const uid = window.__currentUserId__ || '';
        if (f.mine && c.assignedUserId !== uid) return false;
        if (f.unassigned && c.assignedUserId) return false;
        if (f.week) {
            if (!c.lastMessageAt) return false;
            if (Date.now() - new Date(c.lastMessageAt).getTime() > 7 * 864e5) return false;
        }
        if (state.search) {
            const s = state.search.toLowerCase();
            const hay = [c.displayName, c.company, c.phoneNumber, c.email].filter(Boolean).join(' ').toLowerCase();
            if (!hay.includes(s)) return false;
        }
        return true;
    }

    // ── Rendering ────────────────────────────────────────────────────────
    function renderBoard() {
        board.innerHTML = '';
        state.columnEls.clear();

        for (const stage of STAGES) {
            const col = document.createElement('section');
            col.className = `pl-col pl-col-${stage.cls}`;
            col.setAttribute('role', 'listitem');
            col.dataset.stage = stage.id;
            col.setAttribute('aria-label', t(stage.key));

            col.innerHTML = `
                <header class="pl-col-head">
                    <span class="pl-col-accent" aria-hidden="true"></span>
                    <span class="pl-col-name">${escapeHtml(t(stage.key))}</span>
                    <span class="pl-col-count" data-count>0</span>
                    <button type="button" class="pl-col-menu" aria-label="${escapeHtml(t('Pipeline.ColumnMenu'))}" title="${escapeHtml(t('Pipeline.ColumnMenu'))}">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><circle cx="5" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="19" cy="12" r="1.6"/></svg>
                    </button>
                </header>
                <div class="pl-col-list" data-list role="list"></div>
                <a class="pl-col-add" href="/dashboard/contacts">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                    <span>${escapeHtml(t('Pipeline.AddLead'))}</span>
                </a>`;

            const listEl = col.querySelector('[data-list]');
            const countEl = col.querySelector('[data-count]');
            state.columnEls.set(stage.id, { listEl, countEl });

            board.appendChild(col);
        }

        for (const card of state.cards) {
            if (!cardPassesFilters(card)) continue;
            const colInfo = state.columnEls.get(card.lifecycleStage);
            if (colInfo) colInfo.listEl.appendChild(buildCard(card));
        }
        recountAll();
        ensureBoardScrollable();
    }

    // Guarantee the board is a width-constrained horizontal scroller, defeating any flexbox
    // min-content sizing that would otherwise let it grow to fit every column (in which case
    // nothing could scroll). Re-checked on each render and on resize.
    function ensureBoardScrollable() {
        const wrap = board.parentElement;
        if (!wrap) return;
        board.style.overflowX = 'auto';
        board.style.overflowY = 'hidden';
        // If the board grew wider than its container (so scrollWidth === clientWidth), pin its
        // width to the container so the columns overflow and become scrollable.
        if (board.scrollWidth <= board.clientWidth && board.children.length) {
            board.style.width = wrap.clientWidth + 'px';
            board.style.maxWidth = wrap.clientWidth + 'px';
        }
        updateArrowsRef();
    }

    function buildCard(c) {
        const el = document.createElement('article');
        el.className = 'pl-card';
        el.dataset.id = c.id;
        el.dataset.stage = c.lifecycleStage;
        el.tabIndex = 0;
        el.setAttribute('role', 'button');
        el.setAttribute('aria-label', `${c.displayName || c.phoneNumber} — ${t(stageMeta(c.lifecycleStage).key)}`);

        const name = escapeHtml(c.displayName || c.phoneNumber);
        const company = c.company ? `<div class="pl-card-company">${escapeHtml(c.company)}</div>` : '';
        const avatar = c.avatarUrl
            ? `<img class="pl-card-avatar" src="${escapeHtml(c.avatarUrl)}" alt="" />`
            : `<span class="pl-card-avatar pl-card-avatar-initials">${escapeHtml((c.displayName || c.phoneNumber || '?').trim().charAt(0).toUpperCase())}</span>`;

        const ch = [];
        const chDef = CHANNELS[c.channelType];
        if (chDef) ch.push(`<span class="pl-ch" title="${escapeHtml(chDef.label)}"><svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">${chDef.svg}</svg></span>`);
        if (c.hasEmail) ch.push(`<span class="pl-ch" title="Email"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-10 6L2 7"/></svg></span>`);
        if (c.hasPhone) ch.push(`<span class="pl-ch" title="Phone"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z"/></svg></span>`);

        const tags = (c.tags || []).slice(0, 3).map(tg =>
            `<span class="pl-tag" style="--tag:${escapeHtml(tg.color || '#9ca3af')}">${escapeHtml(tg.name)}</span>`).join('');

        el.innerHTML = `
            <div class="pl-card-top">
                ${avatar}
                <div class="pl-card-id">
                    <div class="pl-card-name">${name}</div>
                    ${company}
                </div>
                <div class="pl-card-channels">${ch.join('')}</div>
            </div>
            <div class="pl-card-foot">
                <span class="pl-card-last">${escapeHtml(t('Pipeline.LastContact'))}: ${escapeHtml(relativeTime(c.lastMessageAt))}</span>
            </div>
            ${tags ? `<div class="pl-card-tags">${tags}</div>` : ''}`;

        wireCardPointer(el, c);
        wireCardKeyboard(el, c);
        return el;
    }

    function openCardConversation(c) {
        if (!c.primaryConversationId) return;
        window.location.href = c.primaryInstanceId
            ? `/dashboard/chats?instance=${c.primaryInstanceId}&conversation=${c.primaryConversationId}`
            : `/dashboard/chats?conversation=${c.primaryConversationId}`;
    }

    function recountAll() {
        for (const [stage, info] of state.columnEls) {
            info.countEl.textContent = info.listEl.querySelectorAll('.pl-card').length;
        }
    }

    // ── Drag & drop (Pointer Events) ─────────────────────────────────────
    // Custom pointer-driven drag instead of native HTML5 DnD: a floating "ghost" follows the
    // cursor, the target column is found with elementFromPoint, and — crucially — we own the
    // coordinate stream so edge auto-scroll actually works (native DnD auto-scroll is unreliable
    // and Chrome starves rAF during a drag). Works for mouse, pen, and touch.
    const EDGE = 110;       // px from an edge where auto-scroll kicks in
    const MAX_SPEED = 26;   // px per tick at the very edge
    const DRAG_THRESHOLD = 5; // px of movement before a press becomes a drag (vs a click)
    let lastDragX = 0, lastDragY = 0, autoScrollTimer = null;
    let drag = null; // { card, el, ghost, startX, startY, offsetX, offsetY, width, started }

    function autoScrollTick() {
        if (!drag || !drag.started) return;
        const rect = board.getBoundingClientRect();
        if (lastDragX < rect.left + EDGE) {
            board.scrollLeft -= MAX_SPEED * Math.min(1, (rect.left + EDGE - lastDragX) / EDGE);
        } else if (lastDragX > rect.right - EDGE) {
            board.scrollLeft += MAX_SPEED * Math.min(1, (lastDragX - (rect.right - EDGE)) / EDGE);
        }
        // Vertical scroll inside the column currently under the pointer.
        const overList = columnListUnder(lastDragX, lastDragY);
        if (overList) {
            const lr = overList.getBoundingClientRect();
            if (lastDragY < lr.top + EDGE) overList.scrollTop -= MAX_SPEED * Math.min(1, (lr.top + EDGE - lastDragY) / EDGE);
            else if (lastDragY > lr.bottom - EDGE) overList.scrollTop += MAX_SPEED * Math.min(1, (lastDragY - (lr.bottom - EDGE)) / EDGE);
        }
    }
    function startAutoScroll() {
        if (autoScrollTimer) return;
        autoScrollTimer = setInterval(autoScrollTick, 16);
    }
    function stopAutoScroll() {
        if (autoScrollTimer) { clearInterval(autoScrollTimer); autoScrollTimer = null; }
    }

    function columnUnder(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest('.pl-col') : null;
    }
    function columnListUnder(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest('.pl-col-list') : null;
    }

    function wireCardPointer(el, card) {
        el.addEventListener('pointerdown', (e) => {
            if (e.button !== 0 && e.pointerType === 'mouse') return; // left button / touch / pen only
            const rect = el.getBoundingClientRect();
            drag = {
                card, el,
                startX: e.clientX, startY: e.clientY,
                offsetX: e.clientX - rect.left,
                offsetY: e.clientY - rect.top,
                width: rect.width,
                ghost: null,
                started: false
            };
            window.addEventListener('pointermove', onPointerMove);
            window.addEventListener('pointerup', onPointerUp, { once: true });
            window.addEventListener('pointercancel', onPointerCancel, { once: true });
        });
    }

    function onPointerCancel() {
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
        const d = drag;
        drag = null;
        if (d && d.started) cleanupDrag(d);
    }

    function onPointerMove(e) {
        if (!drag) return;
        if (!drag.started) {
            if (Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < DRAG_THRESHOLD) return;
            beginDrag();
        }
        e.preventDefault();
        lastDragX = e.clientX;
        lastDragY = e.clientY;
        positionGhost();
        highlightColumn();
    }

    function beginDrag() {
        drag.started = true;
        const ghost = drag.el.cloneNode(true);
        ghost.classList.add('pl-card-ghost');
        ghost.style.width = drag.width + 'px';
        document.body.appendChild(ghost);
        drag.ghost = ghost;
        drag.el.classList.add('is-dragging');
        drag.el.setAttribute('aria-grabbed', 'true');
        document.body.classList.add('pl-dragging');
        startAutoScroll();
    }

    function positionGhost() {
        if (!drag || !drag.ghost) return;
        drag.ghost.style.transform =
            `translate(${lastDragX - drag.offsetX}px, ${lastDragY - drag.offsetY}px) rotate(2.5deg)`;
    }

    function highlightColumn() {
        document.querySelectorAll('.pl-col.is-dropzone').forEach(c => c.classList.remove('is-dropzone'));
        const col = columnUnder(lastDragX, lastDragY);
        if (col) col.classList.add('is-dropzone');
    }

    function onPointerUp(e) {
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointercancel', onPointerCancel);
        const d = drag;
        drag = null;
        if (!d) return;

        if (!d.started) {
            // No real movement → treat as a click: open the contact's conversation.
            openCardConversation(d.card);
            return;
        }

        const col = columnUnder(e.clientX, e.clientY);
        cleanupDrag(d);
        if (col) {
            const toStage = parseInt(col.dataset.stage, 10);
            if (Number.isFinite(toStage)) moveCard(d.card.id, toStage);
        }
    }

    function cleanupDrag(d) {
        stopAutoScroll();
        if (d.ghost) d.ghost.remove();
        d.el.classList.remove('is-dragging');
        d.el.removeAttribute('aria-grabbed');
        document.body.classList.remove('pl-dragging');
        document.querySelectorAll('.pl-col.is-dropzone').forEach(c => c.classList.remove('is-dropzone'));
    }

    async function moveCard(id, toStage) {
        const card = state.cards.find(c => c.id === id);
        const el = board.querySelector(`.pl-card[data-id="${id}"]`);
        if (!card || !el) return;

        const fromStage = card.lifecycleStage;
        if (fromStage === toStage) return;

        const fromList = state.columnEls.get(fromStage)?.listEl;
        const toInfo = state.columnEls.get(toStage);
        if (!toInfo) return;

        // Optimistic move + animate into place.
        card.lifecycleStage = toStage;
        el.dataset.stage = toStage;
        toInfo.listEl.appendChild(el);
        el.classList.add('just-dropped');
        setTimeout(() => el.classList.remove('just-dropped'), 320);
        recountAll();

        if (toStage === WON_STAGE) celebrate();

        try {
            const res = await fetch(`/api/contacts/${id}/lifecycle`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ stage: toStage })
            });
            if (!res.ok) throw new Error('HTTP ' + res.status);
        } catch (err) {
            // Revert on failure.
            card.lifecycleStage = fromStage;
            el.dataset.stage = fromStage;
            if (fromList) fromList.appendChild(el);
            recountAll();
            showToast(t('Pipeline.MoveError'), 'error');
        }
    }

    // ── Keyboard accessibility ───────────────────────────────────────────
    // Ctrl/Cmd + ArrowLeft / ArrowRight moves the focused card to the adjacent stage.
    function wireCardKeyboard(el, card) {
        el.addEventListener('keydown', (e) => {
            if (!(e.ctrlKey || e.metaKey)) return;
            let target = null;
            if (e.key === 'ArrowRight') target = Math.min(STAGES.length - 1, card.lifecycleStage + 1);
            else if (e.key === 'ArrowLeft') target = Math.max(0, card.lifecycleStage - 1);
            else return;
            e.preventDefault();
            if (target === card.lifecycleStage) return;
            moveCard(card.id, target).then(() => {
                const moved = board.querySelector(`.pl-card[data-id="${card.id}"]`);
                if (moved) moved.focus();
                showToast(t('Pipeline.MovedTo', t(stageMeta(target).key)), 'info');
            });
        });
    }

    // ── Filters & search wiring ──────────────────────────────────────────
    function wireControls() {
        document.querySelectorAll('.pl-chip').forEach(chip => {
            chip.addEventListener('click', () => {
                const f = chip.dataset.filter;
                if (f === 'mine') { state.filters.mine = !state.filters.mine; if (state.filters.mine) state.filters.unassigned = false; }
                else if (f === 'unassigned') { state.filters.unassigned = !state.filters.unassigned; if (state.filters.unassigned) state.filters.mine = false; }
                else if (f === 'week') { state.filters.week = !state.filters.week; }
                syncChips();
                renderBoard();
            });
        });

        const search = document.getElementById('plSearch');
        let h;
        search?.addEventListener('input', () => {
            clearTimeout(h);
            h = setTimeout(() => { state.search = search.value.trim(); renderBoard(); }, 200);
        });
    }

    function syncChips() {
        document.querySelectorAll('.pl-chip').forEach(chip => {
            const f = chip.dataset.filter;
            const on = (f === 'mine' && state.filters.mine) || (f === 'week' && state.filters.week) || (f === 'unassigned' && state.filters.unassigned);
            chip.classList.toggle('is-active', on);
            chip.setAttribute('aria-pressed', String(on));
        });
    }

    // ── Confetti (no library) ────────────────────────────────────────────
    function celebrate() {
        const canvas = document.getElementById('plConfetti');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;

        const colors = ['#3f6b4a', '#7aa183', '#e9c46a', '#e76f51', '#9b8cce', '#5a9bd4'];
        const pieces = [];
        for (let i = 0; i < 120; i++) {
            pieces.push({
                x: canvas.width / 2 + (Math.random() - 0.5) * 240,
                y: canvas.height / 3,
                vx: (Math.random() - 0.5) * 7,
                vy: Math.random() * -7 - 3,
                size: 5 + Math.random() * 5,
                color: colors[i % colors.length],
                rot: Math.random() * Math.PI,
                vr: (Math.random() - 0.5) * 0.3
            });
        }

        let frame = 0;
        const gravity = 0.18;
        (function tick() {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            pieces.forEach(p => {
                p.vy += gravity; p.x += p.vx; p.y += p.vy; p.rot += p.vr;
                ctx.save();
                ctx.translate(p.x, p.y);
                ctx.rotate(p.rot);
                ctx.fillStyle = p.color;
                ctx.globalAlpha = Math.max(0, 1 - frame / 90);
                ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
                ctx.restore();
            });
            frame++;
            if (frame < 90) requestAnimationFrame(tick);
            else ctx.clearRect(0, 0, canvas.width, canvas.height);
        })();
    }

    // Visible scroll arrows — a guaranteed way to reach off-screen columns. Click for one step,
    // or press-and-hold to keep scrolling. Auto-hide at each end.
    function setupScrollArrows() {
        const wrap = board.parentElement;
        if (!wrap) return;

        const left = document.createElement('button');
        left.type = 'button';
        left.className = 'pl-scroll-btn pl-scroll-btn-left';
        left.setAttribute('aria-label', 'Scroll left');
        left.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';

        const right = document.createElement('button');
        right.type = 'button';
        right.className = 'pl-scroll-btn pl-scroll-btn-right';
        right.setAttribute('aria-label', 'Scroll right');
        right.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

        wrap.appendChild(left);
        wrap.appendChild(right);

        const STEP = 320;
        const hold = (dir) => {
            let timer = null;
            const start = () => {
                board.scrollBy({ left: dir * STEP, behavior: 'smooth' });
                timer = setInterval(() => { board.scrollLeft += dir * 22; updateArrows(); }, 16);
            };
            const stop = () => { if (timer) { clearInterval(timer); timer = null; } updateArrows(); };
            return { start, stop };
        };
        const l = hold(-1), r = hold(1);
        left.addEventListener('pointerdown', l.start);
        right.addEventListener('pointerdown', r.start);
        ['pointerup', 'pointerleave', 'pointercancel'].forEach(ev => {
            left.addEventListener(ev, l.stop);
            right.addEventListener(ev, r.stop);
        });

        function updateArrows() {
            const canScroll = board.scrollWidth > board.clientWidth + 2;
            const atStart = board.scrollLeft <= 2;
            const atEnd = board.scrollLeft >= board.scrollWidth - board.clientWidth - 2;
            left.hidden = !canScroll || atStart;
            right.hidden = !canScroll || atEnd;
        }
        board.addEventListener('scroll', updateArrows);
        window.addEventListener('resize', updateArrows);
        updateArrowsRef = updateArrows;
        updateArrows();
    }
    let updateArrowsRef = () => {};

    // ── Init ─────────────────────────────────────────────────────────────
    function init() {
        if (!board) return;
        wireControls();
        setupScrollArrows();

        // Mouse wheel scrolls the board horizontally (no Shift needed).
        board.addEventListener('wheel', (e) => {
            if (board.scrollWidth <= board.clientWidth) return;
            const delta = Math.abs(e.deltaY) > Math.abs(e.deltaX) ? e.deltaY : e.deltaX;
            if (delta === 0) return;
            board.scrollLeft += delta;
            updateArrowsRef();
            e.preventDefault();
        }, { passive: false });

        window.addEventListener('resize', ensureBoardScrollable);
        loadBoard();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
