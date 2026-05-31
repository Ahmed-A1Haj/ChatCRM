/* Shared Media & Files gallery — driven by /dashboard/chats/{id}/attachments.
   Pure vanilla JS (no new libraries). Renders three tabs: an image thumbnail grid,
   a file list, and a links list, with search / date-range / sender filters,
   "load more" pagination, and a lightbox for full-size image preview. */
(function () {
    'use strict';

    const cfg = window.__mediaGallery__ || {};
    const conversationId = cfg.conversationId;
    const PAGE_SIZE = 24;

    // ── State ────────────────────────────────────────────────────────────
    const state = {
        tab: 'images',     // images | files | links
        page: 1,
        total: 0,
        search: '',
        from: '',
        to: '',
        sender: '',
        loading: false
    };

    // ── Element refs ─────────────────────────────────────────────────────
    const el = {
        tabs: Array.from(document.querySelectorAll('.media-tab')),
        imagesGrid: document.getElementById('mediaImagesGrid'),
        filesList: document.getElementById('mediaFilesList'),
        linksList: document.getElementById('mediaLinksList'),
        loading: document.getElementById('mediaLoading'),
        empty: document.getElementById('mediaEmpty'),
        loadMoreWrap: document.getElementById('mediaLoadMoreWrap'),
        loadMore: document.getElementById('mediaLoadMore'),
        search: document.getElementById('mediaSearch'),
        from: document.getElementById('mediaFrom'),
        to: document.getElementById('mediaTo'),
        sender: document.getElementById('mediaSender'),
        clear: document.getElementById('mediaClearFilters'),
        lightbox: document.getElementById('mediaLightbox'),
        lightboxImg: document.getElementById('mediaLightboxImg'),
        lightboxCaption: document.getElementById('mediaLightboxCaption'),
        lightboxClose: document.getElementById('mediaLightboxClose'),
        lightboxDownload: document.getElementById('mediaLightboxDownload')
    };

    // ── Helpers ──────────────────────────────────────────────────────────
    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function formatBytes(bytes) {
        if (bytes == null) return '';
        if (bytes < 1024) return bytes + ' B';
        const units = ['KB', 'MB', 'GB'];
        let val = bytes / 1024, i = 0;
        while (val >= 1024 && i < units.length - 1) { val /= 1024; i++; }
        return val.toFixed(val < 10 ? 1 : 0) + ' ' + units[i];
    }

    function formatWhen(iso) {
        if (!iso) return '';
        try {
            return window.i18n.formatDate(iso, { year: 'numeric', month: 'short', day: 'numeric' }) +
                ' · ' + window.i18n.formatTime(iso);
        } catch (e) {
            return new Date(iso).toLocaleString();
        }
    }

    function fileNameFromUrl(url) {
        if (!url) return 'file';
        try {
            const clean = url.split('?')[0];
            return decodeURIComponent(clean.substring(clean.lastIndexOf('/') + 1)) || 'file';
        } catch (e) {
            return 'file';
        }
    }

    function showToast(message, type) {
        const stack = document.getElementById('toastStack');
        if (!stack) return;
        const node = document.createElement('div');
        node.className = 'toast toast-' + (type || 'info');
        node.textContent = message;
        stack.appendChild(node);
        setTimeout(() => {
            node.classList.add('toast-out');
            setTimeout(() => node.remove(), 200);
        }, 2400);
    }

    function hasActiveFilters() {
        return !!(state.search || state.from || state.to || state.sender);
    }

    // ── Data fetch ───────────────────────────────────────────────────────
    function buildUrl() {
        const params = new URLSearchParams();
        params.set('type', state.tab);
        params.set('page', String(state.page));
        params.set('pageSize', String(PAGE_SIZE));
        if (state.search) params.set('search', state.search);
        if (state.sender) params.set('sender', state.sender);
        if (state.from) params.set('fromUtc', state.from + 'T00:00:00Z');
        if (state.to) params.set('toUtc', state.to + 'T23:59:59Z');
        return `/dashboard/chats/${conversationId}/attachments?` + params.toString();
    }

    async function load(reset) {
        if (state.loading) return;
        if (reset) state.page = 1;
        state.loading = true;

        // First-page loads clear the surface and show the spinner; "load more" appends.
        if (state.page === 1) {
            clearSurfaces();
            el.empty.classList.add('d-none');
            el.loadMoreWrap.classList.add('d-none');
            el.loading.classList.remove('d-none');
        } else {
            el.loadMore.disabled = true;
            el.loadMore.textContent = t('Media.Loading');
        }

        try {
            const res = await fetch(buildUrl(), { headers: { 'Accept': 'application/json' } });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const data = await res.json();
            state.total = data.total || 0;
            render(data);
        } catch (e) {
            showToast(t('Media.LoadError'), 'error');
        } finally {
            state.loading = false;
            el.loading.classList.add('d-none');
            el.loadMore.disabled = false;
            el.loadMore.textContent = t('Media.LoadMore');
            updateClearButton();
        }
    }

    function clearSurfaces() {
        el.imagesGrid.innerHTML = '';
        el.filesList.innerHTML = '';
        el.linksList.innerHTML = '';
    }

    function activeSurface() {
        if (state.tab === 'images') return el.imagesGrid;
        if (state.tab === 'links') return el.linksList;
        return el.filesList;
    }

    function render(data) {
        const items = (state.tab === 'links' ? data.links : data.items) || [];
        const surface = activeSurface();

        // Toggle the right surface visible, hide the others.
        el.imagesGrid.classList.toggle('d-none', state.tab !== 'images');
        el.filesList.classList.toggle('d-none', state.tab !== 'files');
        el.linksList.classList.toggle('d-none', state.tab !== 'links');

        const html = items.map(state.tab === 'images'
            ? renderImage
            : state.tab === 'links' ? renderLink : renderFile).join('');
        surface.insertAdjacentHTML('beforeend', html);

        const rendered = surface.children.length;
        const isEmpty = rendered === 0;
        el.empty.classList.toggle('d-none', !isEmpty);
        surface.classList.toggle('d-none', isEmpty);

        // Show "load more" while the server still has rows beyond what we've rendered.
        el.loadMoreWrap.classList.toggle('d-none', rendered >= state.total);
    }

    function renderImage(a) {
        const caption = a.caption ? escapeHtml(a.caption) : '';
        return `<button type="button" class="media-thumb" role="listitem"
                    data-url="${escapeHtml(a.mediaUrl)}"
                    data-caption="${caption}"
                    title="${escapeHtml(a.senderName || '')} · ${escapeHtml(formatWhen(a.sentAt))}">
                    <img src="${escapeHtml(a.mediaUrl)}" alt="${caption}" loading="lazy" />
                    <span class="media-thumb-meta">
                        <span class="media-thumb-sender">${escapeHtml(a.senderName || '')}</span>
                        <span class="media-thumb-date">${escapeHtml(formatWhen(a.sentAt))}</span>
                    </span>
                </button>`;
    }

    function renderFile(a) {
        const name = escapeHtml(a.mediaFileName || fileNameFromUrl(a.mediaUrl));
        const size = a.sizeBytes != null ? `<span class="media-file-size">${escapeHtml(formatBytes(a.sizeBytes))}</span>` : '';
        const ext = (a.mediaFileName || fileNameFromUrl(a.mediaUrl)).split('.').pop().toUpperCase().slice(0, 4);
        return `<div class="media-file-row" role="listitem">
                    <span class="media-file-icon" data-ext="${escapeHtml(ext)}" aria-hidden="true">${escapeHtml(ext)}</span>
                    <span class="media-file-main">
                        <span class="media-file-name" title="${name}">${name}</span>
                        <span class="media-file-sub">
                            <span class="media-file-sender">${escapeHtml(a.senderName || '')}</span>
                            <span class="media-file-dot">·</span>
                            <span class="media-file-date">${escapeHtml(formatWhen(a.sentAt))}</span>
                            ${size ? '<span class="media-file-dot">·</span>' + size : ''}
                        </span>
                    </span>
                    <a class="media-file-download" href="${escapeHtml(a.mediaUrl)}" download="${name}"
                       title="${escapeHtml(t('Media.Download'))}" aria-label="${escapeHtml(t('Media.Download'))}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>
                        </svg>
                    </a>
                </div>`;
    }

    function renderLink(l) {
        const url = escapeHtml(l.url);
        let host = url;
        try { host = new URL(l.url).host; } catch (e) { /* keep full url */ }
        return `<div class="media-link-row" role="listitem">
                    <span class="media-link-icon" aria-hidden="true">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>
                        </svg>
                    </span>
                    <span class="media-link-main">
                        <a class="media-link-url" href="${url}" target="_blank" rel="noopener noreferrer" title="${url}">${escapeHtml(host)}</a>
                        <span class="media-file-sub">
                            <span class="media-file-sender">${escapeHtml(l.senderName || '')}</span>
                            <span class="media-file-dot">·</span>
                            <span class="media-file-date">${escapeHtml(formatWhen(l.sentAt))}</span>
                        </span>
                    </span>
                </div>`;
    }

    // ── Tabs ─────────────────────────────────────────────────────────────
    function switchTab(tab) {
        if (tab === state.tab) return;
        state.tab = tab;
        el.tabs.forEach(b => {
            const active = b.dataset.tab === tab;
            b.classList.toggle('active', active);
            b.setAttribute('aria-selected', String(active));
        });
        load(true);
    }

    // ── Lightbox ─────────────────────────────────────────────────────────
    function openLightbox(url, caption) {
        el.lightboxImg.src = url;
        el.lightboxImg.alt = caption || '';
        el.lightboxCaption.textContent = caption || '';
        el.lightboxCaption.classList.toggle('d-none', !caption);
        el.lightboxDownload.href = url;
        el.lightboxDownload.setAttribute('download', fileNameFromUrl(url));
        el.lightbox.classList.remove('d-none');
        document.body.classList.add('media-lightbox-open');
    }

    function closeLightbox() {
        el.lightbox.classList.add('d-none');
        el.lightboxImg.src = '';
        document.body.classList.remove('media-lightbox-open');
    }

    // ── Filters ──────────────────────────────────────────────────────────
    function updateClearButton() {
        el.clear.classList.toggle('d-none', !hasActiveFilters());
    }

    function debounce(fn, ms) {
        let h;
        return function () {
            clearTimeout(h);
            const args = arguments;
            h = setTimeout(() => fn.apply(this, args), ms);
        };
    }

    // ── Wire-up ──────────────────────────────────────────────────────────
    function init() {
        if (!conversationId) return;

        el.tabs.forEach(b => b.addEventListener('click', () => switchTab(b.dataset.tab)));

        el.search.addEventListener('input', debounce(() => {
            state.search = el.search.value.trim();
            load(true);
        }, 300));

        el.from.addEventListener('change', () => { state.from = el.from.value; load(true); });
        el.to.addEventListener('change', () => { state.to = el.to.value; load(true); });
        el.sender.addEventListener('change', () => { state.sender = el.sender.value; load(true); });

        el.clear.addEventListener('click', () => {
            state.search = state.from = state.to = state.sender = '';
            el.search.value = ''; el.from.value = ''; el.to.value = ''; el.sender.value = '';
            load(true);
        });

        el.loadMore.addEventListener('click', () => { state.page++; load(false); });

        // Image thumbnails open the lightbox (event-delegated for appended items).
        el.imagesGrid.addEventListener('click', (e) => {
            const thumb = e.target.closest('.media-thumb');
            if (!thumb) return;
            openLightbox(thumb.dataset.url, thumb.dataset.caption);
        });

        el.lightboxClose.addEventListener('click', closeLightbox);
        el.lightbox.addEventListener('click', (e) => { if (e.target === el.lightbox) closeLightbox(); });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !el.lightbox.classList.contains('d-none')) closeLightbox();
        });

        load(true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
