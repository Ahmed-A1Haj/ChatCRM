/* ─── State ─────────────────────────────────────────────────────────── */
const state = {
    page: 1,
    pageSize: 25,
    sort: 'lastMessage',
    direction: 'desc',
    search: '',
    lifecycle: '',
    assignedUserId: '',
    status: '',
    blocked: ''
};

const STAGES = [
    { id: 0,  name: 'New Client',          cls: 'gray'    },
    { id: 1,  name: 'Not Responding',      cls: 'red'     },
    { id: 2,  name: 'Interested',          cls: 'blue'    },
    { id: 3,  name: 'Thinking',            cls: 'yellow'  },
    { id: 4,  name: 'Wants a Meeting',     cls: 'purple'  },
    { id: 5,  name: 'Waiting for Meeting', cls: 'orange'  },
    { id: 6,  name: 'Discussed',           cls: 'teal'    },
    { id: 7,  name: 'Potential Client',    cls: 'indigo'  },
    { id: 8,  name: 'Will Make Payment',   cls: 'green'   },
    { id: 9,  name: 'Waiting for Contract',cls: 'amber'   },
    { id: 10, name: 'Our Client',          cls: 'emerald' }
];

const CHANNEL_DEFS = [
    { id: 0, name: 'WhatsApp',  icon: 'channel-whatsapp',  cls: 'ch-wa' },
    { id: 1, name: 'Instagram', icon: 'channel-instagram', cls: 'ch-ig' },
    { id: 2, name: 'Messenger', icon: 'channel-messenger', cls: 'ch-fb' }
];

const ICONS = {
    'eye':         '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12" style="display:none"/><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>',
    'user':        '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
    'shield-off':  '<path d="M19.69 14a6.9 6.9 0 0 0 .31-2V5l-8-3-3.16 1.18"/><path d="M4.73 4.73L4 5v7c0 6 8 10 8 10a20.29 20.29 0 0 0 5.62-4.38"/><line x1="1" y1="1" x2="23" y2="23"/>',
    'shield':      '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
    'trash':       '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>',
    'more':        '<circle cx="12" cy="12" r="1"/><circle cx="12" cy="5" r="1"/><circle cx="12" cy="19" r="1"/>',
    'check':       '<polyline points="20 6 9 17 4 12"/>',
    'channel-whatsapp':  '<path d="M17.6 6.32A7.85 7.85 0 0 0 12.05 4a7.94 7.94 0 0 0-6.88 11.93L4 20l4.18-1.1a7.93 7.93 0 0 0 3.86 1h.01a7.94 7.94 0 0 0 7.95-7.93 7.88 7.88 0 0 0-2.4-5.65zM12.05 18.55h-.01a6.6 6.6 0 0 1-3.36-.92l-.24-.14-2.5.65.67-2.43-.16-.25a6.59 6.59 0 1 1 12.23-3.5 6.6 6.6 0 0 1-6.63 6.59zm3.62-4.93c-.2-.1-1.18-.58-1.36-.65-.18-.07-.31-.1-.45.1-.13.2-.51.65-.62.78-.12.13-.23.15-.42.05-.2-.1-.84-.31-1.6-.99a6 6 0 0 1-1.1-1.38c-.12-.2-.01-.3.09-.4.1-.09.2-.23.3-.35.1-.12.13-.2.2-.34.07-.13.03-.25-.02-.35-.05-.1-.45-1.08-.62-1.48-.16-.39-.33-.34-.45-.34l-.39-.01a.74.74 0 0 0-.54.25 2.27 2.27 0 0 0-.7 1.68c0 .99.71 1.95.82 2.08.1.13 1.41 2.16 3.43 3.03.48.2.85.33 1.14.42.48.15.92.13 1.27.08.39-.06 1.18-.48 1.35-.95.17-.46.17-.86.12-.94-.06-.08-.18-.13-.38-.23z"/>',
    'channel-instagram': '<path d="M12 2.16c3.2 0 3.58 0 4.85.07 1.17.05 1.8.25 2.23.41.56.22.96.48 1.38.9.42.42.68.82.9 1.38.16.43.36 1.06.41 2.23.06 1.27.07 1.65.07 4.85s-.01 3.58-.07 4.85c-.05 1.17-.25 1.8-.41 2.23a3.7 3.7 0 0 1-.9 1.38c-.42.42-.82.68-1.38.9-.43.16-1.06.36-2.23.41-1.27.06-1.65.07-4.85.07s-3.58-.01-4.85-.07c-1.17-.05-1.8-.25-2.23-.41a3.71 3.71 0 0 1-1.38-.9 3.7 3.7 0 0 1-.9-1.38c-.16-.43-.36-1.06-.41-2.23-.06-1.27-.07-1.65-.07-4.85s.01-3.58.07-4.85c.05-1.17.25-1.8.41-2.23.22-.56.48-.96.9-1.38.42-.42.82-.68 1.38-.9.43-.16 1.06-.36 2.23-.41C8.42 2.17 8.8 2.16 12 2.16zm0 5.92a4.08 4.08 0 1 0 0 8.16 4.08 4.08 0 0 0 0-8.16zm5.19-.16a.95.95 0 1 1-1.91 0 .95.95 0 0 1 1.91 0z"/>',
    'channel-messenger': '<path d="M12 2C6.36 2 2 6.13 2 11.7c0 2.91 1.19 5.44 3.14 7.18.16.14.27.34.27.56v3.16c0 .25.27.4.49.27l3.42-2.07a.65.65 0 0 1 .55-.07 11 11 0 0 0 2.13.21c5.64 0 10-4.13 10-9.7C22 6.13 17.64 2 12 2zm6.04 7.15-2.93 4.66a1.5 1.5 0 0 1-2.16.4l-2.33-1.75a.6.6 0 0 0-.72 0l-3.15 2.4c-.42.32-.97-.18-.69-.62l2.94-4.66a1.5 1.5 0 0 1 2.16-.4l2.33 1.75a.6.6 0 0 0 .72 0l3.15-2.4c.42-.32.97.18.68.62z"/>'
};

const BRAND_FILL_ICONS = new Set(['channel-whatsapp', 'channel-instagram', 'channel-messenger']);

const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

/* ─── Toasts ───────────────────────────────────────────────────────── */
function showToast(message, type = 'info') {
    const stack = document.getElementById('toastStack');
    if (!stack) return;
    const t = document.createElement('div');
    t.className = `toast toast-${type}`;
    t.textContent = message;
    stack.appendChild(t);
    setTimeout(() => {
        t.classList.add('toast-out');
        setTimeout(() => t.remove(), 200);
    }, 2400);
}

/* ─── Format helpers ──────────────────────────────────────────────── */
function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function fmtDateTime(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    return d.toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function fmtDate(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

function muted(text) { return `<span class="cell-muted">${escapeHtml(text)}</span>`; }

function truncate(text, n = 30) {
    if (!text) return null;
    return text.length > n ? text.slice(0, n) + '…' : text;
}

function svgIcon(name, size = 14) {
    const body = ICONS[name];
    if (!body) return '';
    const isFill = BRAND_FILL_ICONS.has(name);
    if (isFill) {
        return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">${body}</svg>`;
    }
    return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;
}

function avatarInitials(text) {
    if (!text) return '?';
    const parts = text.trim().split(/\s+/).slice(0, 2);
    return parts.map(p => p.charAt(0).toUpperCase()).join('') || '?';
}

/* ─── Fetch + render ──────────────────────────────────────────────── */
async function loadContacts() {
    const q = new URLSearchParams();
    q.set('page', state.page);
    q.set('pageSize', state.pageSize);
    q.set('sort', state.sort);
    q.set('direction', state.direction);
    if (state.search)         q.set('search', state.search);
    if (state.lifecycle)      q.set('lifecycle', state.lifecycle);
    if (state.assignedUserId) q.set('assignedUserId', state.assignedUserId);
    if (state.status !== '')  q.set('status', state.status);
    if (state.blocked !== '') q.set('blocked', state.blocked);

    const tbody = document.getElementById('contactsBody');
    tbody.innerHTML = '<tr><td colspan="10" class="contacts-empty">Loading…</td></tr>';

    try {
        const resp = await fetch('/api/contacts?' + q.toString());
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        const result = await resp.json();
        renderRows(result.items);
        renderPagination(result);
        updateSortHeaders();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="10" class="contacts-empty">Failed to load: ${escapeHtml(e.message)}</td></tr>`;
    }
}

function renderRows(items) {
    const tbody = document.getElementById('contactsBody');
    if (!items.length) {
        tbody.innerHTML = '<tr><td colspan="10" class="contacts-empty">No contacts match these filters.</td></tr>';
        return;
    }

    tbody.innerHTML = items.map(c => renderRow(c)).join('');
}

function renderRow(c) {
    const initials = avatarInitials(c.displayName || c.phoneNumber);

    // Channel cell — colored pill with platform icon
    const channel = CHANNEL_DEFS[c.channelType] ?? { name: c.channel || 'Unknown', icon: null, cls: 'ch-default' };
    const channelHtml = `
        <span class="cell-channel ${channel.cls}">
            ${channel.icon ? svgIcon(channel.icon, 12) : ''}
            <span>${escapeHtml(channel.name)}</span>
        </span>`;

    // Last message — preview + timestamp stacked
    const preview = truncate(c.lastMessagePreview, 35);
    const ts = fmtDateTime(c.lastMessageAt);
    const lastMsgHtml = preview
        ? `<div class="cell-last">
              <span class="cell-last-text">${escapeHtml(preview)}</span>
              <span class="cell-last-time">${escapeHtml(ts || '')}</span>
           </div>`
        : `<span class="cell-muted">No messages yet</span>`;

    // Lifecycle — colored badge button
    const stage = STAGES[c.lifecycleStage] ?? STAGES[0];
    const lifecycleHtml = `
        <button class="badge-btn lc-badge lc-${stage.cls}" type="button"
                data-popover="lifecycle" data-id="${c.id}" data-current="${c.lifecycleStage}">
            <span class="badge-dot"></span>
            <span>${escapeHtml(stage.name)}</span>
            <span class="chev">▾</span>
        </button>`;

    // Assignee — avatar + name OR Unassigned badge
    let assigneeHtml;
    if (c.assignedUserName) {
        const assigneeInitials = avatarInitials(c.assignedUserName);
        assigneeHtml = `
            <button class="assignee-btn" type="button" data-popover="assignee" data-id="${c.id}" data-current="${escapeHtml(c.assignedUserId || '')}">
                <span class="assignee-avatar">${escapeHtml(assigneeInitials)}</span>
                <span class="assignee-name">${escapeHtml(c.assignedUserName)}</span>
                <span class="chev">▾</span>
            </button>`;
    } else {
        assigneeHtml = `
            <button class="badge-btn unassigned-badge" type="button" data-popover="assignee" data-id="${c.id}" data-current="">
                <span>Unassigned</span>
                <span class="chev">▾</span>
            </button>`;
    }

    // Status — green Open / grey Closed
    const isClosed = c.conversationStatus === 2;
    const statusCls = isClosed ? 'status-closed' : 'status-open';
    const statusLabel = isClosed ? 'Closed' : 'Open';
    const statusHtml = `
        <button class="badge-btn status-badge ${statusCls}" type="button"
                data-popover="status" data-id="${c.id}" data-current="${isClosed ? 2 : 0}">
            <span class="status-dot-inline"></span>
            <span>${statusLabel}</span>
            <span class="chev">▾</span>
        </button>`;

    // Country / Language with muted "Unknown" fallback
    const countryHtml = c.country ? escapeHtml(c.country) : muted('Unknown');
    const langHtml = `
        <input type="text" class="cell-lang-input"
               value="${escapeHtml(c.language || '')}"
               placeholder="Unknown"
               data-action="lang" data-id="${c.id}" />`;

    // Blocked tag inline next to name
    const blockedTag = c.isBlocked ? '<span class="cell-blocked-tag">Blocked</span>' : '';

    return `
    <tr data-id="${c.id}" data-conv-id="${c.primaryConversationId ?? ''}" data-instance-id="${c.primaryInstanceId ?? ''}" class="${c.isBlocked ? 'is-blocked' : ''}">
        <td>
            <div class="cell-contact">
                <span class="cell-avatar">${escapeHtml(initials)}</span>
                <div class="cell-contact-text">
                    <span class="cell-contact-name">${escapeHtml(c.displayName || c.phoneNumber)}${blockedTag}</span>
                    <span class="cell-contact-phone">+${escapeHtml(c.phoneNumber)}</span>
                </div>
            </div>
        </td>
        <td>${channelHtml}</td>
        <td>${lastMsgHtml}</td>
        <td class="cell-time-only">${escapeHtml(fmtDate(c.createdAt) || '—')}</td>
        <td>${countryHtml}</td>
        <td>${langHtml}</td>
        <td>${lifecycleHtml}</td>
        <td>${assigneeHtml}</td>
        <td>${statusHtml}</td>
        <td class="cell-actions">
            <div class="action-menu-wrap">
                <button class="action-trigger" type="button" data-popover="actions" data-id="${c.id}" aria-label="Actions">
                    ${svgIcon('more', 16)}
                </button>
            </div>
        </td>
    </tr>`;
}

function renderPagination(result) {
    const { total, page, pageSize } = result;
    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
    const to   = Math.min(page * pageSize, total);

    document.getElementById('pageInfo').textContent =
        total === 0 ? 'No contacts' : `${from}–${to} of ${total}`;
    document.getElementById('pageLabel').textContent = `Page ${page} of ${totalPages}`;
    document.getElementById('prevPage').disabled = page <= 1;
    document.getElementById('nextPage').disabled = page >= totalPages;
}

function updateSortHeaders() {
    document.querySelectorAll('th.sortable').forEach(th => {
        th.classList.remove('is-sorted', 'asc');
        if (th.dataset.sort === state.sort) {
            th.classList.add('is-sorted');
            if (state.direction === 'asc') th.classList.add('asc');
        }
    });
}

/* ─── Popover dropdowns (custom — not a native <select>) ──────────── */
function closeAllPopovers() {
    document.querySelectorAll('.cm-popover').forEach(p => p.remove());
}

function buildLifecyclePopover(currentId, anchorId) {
    const items = STAGES.map(s => `
        <button class="cm-popover-item" data-stage="${s.id}">
            <span class="lc-dot lc-bg-${s.cls}"></span>
            <span>${s.name}</span>
            ${s.id === currentId ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
        </button>`).join('');

    return `<div class="cm-popover cm-popover-md" data-anchor="${anchorId}" role="menu">${items}</div>`;
}

function buildStatusPopover(currentId, anchorId) {
    const items = [
        { id: 0, label: 'Open',   cls: 'status-open'   },
        { id: 2, label: 'Closed', cls: 'status-closed' }
    ].map(s => `
        <button class="cm-popover-item" data-status="${s.id}">
            <span class="status-dot-inline ${s.cls}"></span>
            <span>${s.label}</span>
            ${s.id === currentId ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
        </button>`).join('');

    return `<div class="cm-popover cm-popover-sm" data-anchor="${anchorId}" role="menu">${items}</div>`;
}

function buildAssigneePopover(currentUserId, anchorId) {
    const me = window.__CURRENT_USER_ID__;
    const team = window.__TEAM__ || [];
    const myEntry = team.find(u => u.id === me);
    const others = team.filter(u => u.id !== me);

    const meBtn = (me && myEntry)
        ? `<button class="cm-popover-item" data-assign="${escapeHtml(me)}">
              <span class="assignee-avatar sm">${escapeHtml(avatarInitials(myEntry.name))}</span>
              <span>Assign to me</span>
              ${currentUserId === me ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
           </button>`
        : '';

    const otherItems = others.map(u => `
        <button class="cm-popover-item" data-assign="${escapeHtml(u.id)}">
            <span class="assignee-avatar sm">${escapeHtml(avatarInitials(u.name))}</span>
            <span>${escapeHtml(u.name)}</span>
            ${currentUserId === u.id ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
        </button>`).join('');

    const unassign = `
        <button class="cm-popover-item" data-assign="">
            <span class="assignee-avatar sm assignee-none">—</span>
            <span>Unassigned</span>
            ${!currentUserId ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
        </button>`;

    return `<div class="cm-popover cm-popover-md" data-anchor="${anchorId}" role="menu">
                ${meBtn}
                ${meBtn ? '<div class="cm-popover-sep"></div>' : ''}
                ${otherItems}
                <div class="cm-popover-sep"></div>
                ${unassign}
            </div>`;
}

function buildActionsPopover(id, isBlocked, anchorId) {
    return `<div class="cm-popover cm-popover-actions" data-anchor="${anchorId}" role="menu">
        <button class="cm-popover-item" data-action="view-details">
            ${svgIcon('eye', 14)}<span>View details</span>
        </button>
        <button class="cm-popover-item" data-action="view-contact">
            ${svgIcon('user', 14)}<span>View contact</span>
        </button>
        <div class="cm-popover-sep"></div>
        <button class="cm-popover-item" data-action="block">
            ${isBlocked ? svgIcon('shield', 14) : svgIcon('shield-off', 14)}
            <span>${isBlocked ? 'Unblock contact' : 'Block contact'}</span>
        </button>
        <button class="cm-popover-item is-danger" data-action="delete">
            ${svgIcon('trash', 14)}<span>Delete contact</span>
        </button>
    </div>`;
}

function showPopover(anchor, html) {
    closeAllPopovers();
    const wrap = anchor.closest('.action-menu-wrap, .cell-popover-host');
    const host = wrap || anchor;
    if (!host.classList.contains('cell-popover-host') && !wrap) {
        host.classList.add('cell-popover-host');
    }
    host.insertAdjacentHTML('beforeend', html);
}

document.addEventListener('click', (e) => {
    const trigger = e.target.closest('[data-popover]');
    if (!trigger) {
        if (!e.target.closest('.cm-popover')) closeAllPopovers();
        return;
    }

    const kind = trigger.dataset.popover;
    const id = parseInt(trigger.dataset.id, 10);
    const existing = trigger.parentElement.querySelector('.cm-popover');
    closeAllPopovers();
    if (existing) return; // toggle off

    // The popover needs a host. For badge buttons we make the parent <td> the host briefly.
    let host = trigger.closest('.action-menu-wrap');
    if (!host) {
        host = trigger.parentElement;
        host.classList.add('cell-popover-host');
    }

    if (kind === 'lifecycle') {
        const current = parseInt(trigger.dataset.current, 10);
        host.insertAdjacentHTML('beforeend', buildLifecyclePopover(current, id));
    } else if (kind === 'status') {
        const current = parseInt(trigger.dataset.current, 10);
        host.insertAdjacentHTML('beforeend', buildStatusPopover(current, id));
    } else if (kind === 'assignee') {
        const current = trigger.dataset.current || null;
        host.insertAdjacentHTML('beforeend', buildAssigneePopover(current, id));
    } else if (kind === 'actions') {
        const row = trigger.closest('tr');
        const isBlocked = row?.classList.contains('is-blocked') || false;
        host.insertAdjacentHTML('beforeend', buildActionsPopover(id, isBlocked, id));
    }
});

/* ─── Popover item click handlers ───────────────────────────────────── */
async function postJson(url, body, method = 'POST') {
    return fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
        body: body == null ? null : JSON.stringify(body)
    });
}

document.addEventListener('click', async (e) => {
    const item = e.target.closest('.cm-popover-item');
    if (!item) return;

    const popover = item.closest('.cm-popover');
    const trigger = popover?.parentElement?.querySelector('[data-popover]');
    if (!trigger) return;
    const id = parseInt(trigger.dataset.id, 10);

    // Lifecycle
    if (item.dataset.stage !== undefined) {
        const stage = parseInt(item.dataset.stage, 10);
        const resp = await postJson(`/api/contacts/${id}/lifecycle`, { stage });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update lifecycle.', 'error');
        } else {
            showToast(`Lifecycle: ${STAGES[stage].name}`, 'success');
            loadContacts();
        }
        closeAllPopovers();
        return;
    }

    // Status
    if (item.dataset.status !== undefined) {
        const status = parseInt(item.dataset.status, 10);
        const resp = await postJson(`/api/contacts/${id}/status`, { status });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update status.', 'error');
        } else {
            showToast(status === 2 ? 'Marked as closed' : 'Reopened', 'success');
            loadContacts();
        }
        closeAllPopovers();
        return;
    }

    // Assignee
    if (item.dataset.assign !== undefined) {
        const userId = item.dataset.assign;
        const resp = await postJson(`/api/contacts/${id}/assign`, { userId: userId || null });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to assign.', 'error');
        } else {
            showToast(userId ? 'Assigned' : 'Unassigned', 'success');
            loadContacts();
        }
        closeAllPopovers();
        return;
    }

    // Actions menu items
    if (item.dataset.action) {
        const row = trigger.closest('tr');
        const action = item.dataset.action;
        closeAllPopovers();

        if (action === 'view-details') {
            // Open the contact-details modal in place (no navigation).
            const convId = row?.dataset.convId;
            const instId = row?.dataset.instanceId;
            openContactDetails(convId, instId);
            return;
        }

        if (action === 'view-contact') {
            // Deep-link straight into the chat conversation.
            const convId = row?.dataset.convId;
            const instId = row?.dataset.instanceId;
            if (!convId || !instId) {
                showToast('No conversation found for this contact.', 'error');
                return;
            }
            window.location.href = `/dashboard/chats?instance=${instId}&conversation=${convId}`;
            return;
        }

        if (action === 'block') {
            const isBlocked = row?.classList.contains('is-blocked') || false;
            const next = !isBlocked;
            const name = row?.querySelector('.cell-contact-name')?.textContent.trim() || 'this contact';
            openConfirm({
                title: next ? 'Block contact?' : 'Unblock contact?',
                body: `<strong>${escapeHtml(name)}</strong>`,
                bullets: next
                    ? ['No further messages will be processed', 'Existing chat history is kept', 'You can unblock at any time']
                    : ['Messages from this contact will resume', 'You can re-block at any time'],
                confirmLabel: next ? 'Block' : 'Unblock',
                onConfirm: async () => {
                    const resp = await postJson(`/api/contacts/${id}/block`, { blocked: next });
                    if (!resp.ok) {
                        const err = await resp.json().catch(() => ({}));
                        showToast(err.error || 'Failed.', 'error');
                        return;
                    }
                    showToast(next ? 'Contact blocked' : 'Contact unblocked', 'success');
                    loadContacts();
                }
            });
        }

        if (action === 'delete') {
            const name = row?.querySelector('.cell-contact-name')?.textContent.trim() || 'this contact';
            openConfirm({
                title: 'Delete contact?',
                body: `<strong>${escapeHtml(name)}</strong>`,
                bullets: ['All conversations and messages will be deleted', 'This cannot be undone'],
                confirmLabel: 'Delete forever',
                isDanger: true,
                onConfirm: async () => {
                    const resp = await fetch(`/api/contacts/${id}`, {
                        method: 'DELETE',
                        headers: { 'RequestVerificationToken': token() }
                    });
                    if (!resp.ok) {
                        const err = await resp.json().catch(() => ({}));
                        showToast(err.error || 'Delete failed.', 'error');
                        return;
                    }
                    showToast('Contact deleted', 'success');
                    loadContacts();
                }
            });
        }
    }
});

/* ─── Inline language editor ────────────────────────────────────────── */
document.addEventListener('change', async (e) => {
    const el = e.target;
    if (!el.matches('input[data-action="lang"]')) return;
    const id = parseInt(el.dataset.id, 10);
    if (!id) return;
    try {
        const resp = await postJson(`/api/contacts/${id}/language`, { contactId: id, language: el.value });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update.', 'error');
            return;
        }
        showToast('Language updated', 'success');
    } catch (err) {
        showToast('Network error: ' + err.message, 'error');
    }
});

/* ─── Confirmation modal ─────────────────────────────────────────── */
let pendingConfirm = null;

function openConfirm({ title, body, bullets, confirmLabel, isDanger, onConfirm }) {
    pendingConfirm = onConfirm;
    document.getElementById('confirmTitle').textContent = title;
    document.getElementById('confirmBody').innerHTML = body || '';
    document.getElementById('confirmList').innerHTML = (bullets || []).map(b => `<li>${escapeHtml(b)}</li>`).join('');
    const btn = document.getElementById('confirmAction');
    btn.textContent = confirmLabel || 'Confirm';
    btn.className = 'btn-ghost ' + (isDanger ? 'btn-danger' : '');
    document.getElementById('confirmModal').classList.remove('d-none');
}

function closeConfirmModal() {
    pendingConfirm = null;
    document.getElementById('confirmModal').classList.add('d-none');
}
window.closeConfirmModal = closeConfirmModal;

document.getElementById('confirmAction')?.addEventListener('click', async () => {
    const fn = pendingConfirm;
    closeConfirmModal();
    if (typeof fn === 'function') await fn();
});

/* ─── Filters / search / sort / paging ──────────────────────────────── */
function debounce(fn, ms) {
    let t;
    return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
}

document.getElementById('searchInput').addEventListener('input', debounce((e) => {
    state.search = e.target.value.trim();
    state.page = 1;
    loadContacts();
}, 300));

['lifecycleFilter', 'assigneeFilter', 'statusFilter', 'blockedFilter'].forEach(id => {
    document.getElementById(id).addEventListener('change', (e) => {
        const map = { lifecycleFilter: 'lifecycle', assigneeFilter: 'assignedUserId', statusFilter: 'status', blockedFilter: 'blocked' };
        state[map[id]] = e.target.value;
        state.page = 1;
        loadContacts();
    });
});

document.getElementById('pageSize').addEventListener('change', (e) => {
    state.pageSize = parseInt(e.target.value, 10);
    state.page = 1;
    loadContacts();
});

document.querySelectorAll('th.sortable').forEach(th => {
    th.addEventListener('click', () => {
        const newSort = th.dataset.sort;
        if (state.sort === newSort) state.direction = state.direction === 'asc' ? 'desc' : 'asc';
        else { state.sort = newSort; state.direction = 'desc'; }
        loadContacts();
    });
});

document.getElementById('prevPage').addEventListener('click', () => {
    if (state.page > 1) { state.page--; loadContacts(); }
});

document.getElementById('nextPage').addEventListener('click', () => {
    state.page++;
    loadContacts();
});

/* ─── Export ────────────────────────────────────────────────────────── */
document.getElementById('exportBtn').addEventListener('click', async () => {
    const btn = document.getElementById('exportBtn');
    const spinner = btn.querySelector('.btn-spinner');
    const label = btn.querySelector('.export-label');
    btn.disabled = true;
    spinner.classList.remove('d-none');
    label.textContent = 'Exporting…';

    const q = new URLSearchParams();
    q.set('sort', state.sort);
    q.set('direction', state.direction);
    if (state.search)         q.set('search', state.search);
    if (state.lifecycle)      q.set('lifecycle', state.lifecycle);
    if (state.assignedUserId) q.set('assignedUserId', state.assignedUserId);
    if (state.status !== '')  q.set('status', state.status);
    if (state.blocked !== '') q.set('blocked', state.blocked);

    try {
        const resp = await fetch('/api/contacts/export?' + q.toString());
        if (!resp.ok) throw new Error('Export failed');
        const blob = await resp.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `contacts-${new Date().toISOString().slice(0, 10)}.csv`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
        showToast('Export ready', 'success');
    } catch (e) {
        showToast('Export failed: ' + e.message, 'error');
    } finally {
        btn.disabled = false;
        spinner.classList.add('d-none');
        label.textContent = 'Export to Excel';
    }
});

/* ─── Boot ──────────────────────────────────────────────────────────── */
loadContacts();

/* ─── Contact Details modal ───────────────────────────────────────── */
function openContactDetails(conversationId, instanceId) {
    const modal = document.getElementById('contactDetailsModal');
    if (!modal) return;

    document.getElementById('cdLoading').classList.remove('d-none');
    document.getElementById('cdContent').classList.add('d-none');
    modal.classList.remove('d-none');

    if (!conversationId) {
        document.getElementById('cdLoading').textContent = 'No primary conversation for this contact yet.';
        return;
    }

    // Wire the "Open conversation →" link
    const openLink = document.getElementById('cdOpenChat');
    if (openLink) {
        openLink.href = instanceId
            ? `/dashboard/chats?instance=${instanceId}&conversation=${conversationId}`
            : '/dashboard/chats';
    }

    fetch(`/dashboard/chats/${conversationId}/contact`)
        .then(r => r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)))
        .then(d => populateDetails(d))
        .catch(err => {
            document.getElementById('cdLoading').textContent = 'Failed to load: ' + err.message;
        });
}

function closeContactDetailsModal() {
    document.getElementById('contactDetailsModal')?.classList.add('d-none');
}
window.closeContactDetailsModal = closeContactDetailsModal;

function populateDetails(d) {
    document.getElementById('cdLoading').classList.add('d-none');
    document.getElementById('cdContent').classList.remove('d-none');

    const initials = avatarInitials(d.displayName || d.phoneNumber);
    document.getElementById('cdAvatar').textContent = initials;
    document.getElementById('cdName').textContent  = d.displayName || d.phoneNumber;
    document.getElementById('cdPhone').textContent = d.phoneNumber ? '+' + d.phoneNumber : '—';

    // Lifecycle
    const stage = STAGES[d.lifecycleStage] ?? STAGES[0];
    document.getElementById('cdLifecycle').innerHTML =
        `<span class="badge-btn lc-${stage.cls}" style="cursor:default"><span class="badge-dot"></span>${escapeHtml(stage.name)}</span>`;

    // Channel
    document.getElementById('cdChannel').textContent = d.instanceDisplayName || '—';

    // Country / Language with muted Unknown
    document.getElementById('cdCountry').innerHTML = d.country
        ? escapeHtml(d.country)
        : '<span class="cell-muted">Unknown</span>';
    document.getElementById('cdLanguage').innerHTML = d.language
        ? escapeHtml(d.language)
        : '<span class="cell-muted">Unknown</span>';

    // Assignee
    if (d.assignedUserName) {
        document.getElementById('cdAssigned').innerHTML =
            `<span class="cd-assignee"><span class="assignee-avatar">${escapeHtml(avatarInitials(d.assignedUserName))}</span>${escapeHtml(d.assignedUserName)}</span>`;
    } else {
        document.getElementById('cdAssigned').innerHTML = '<span class="cell-muted">Unassigned</span>';
    }

    // Status
    const isClosed = d.conversationStatus === 2;
    document.getElementById('cdStatus').innerHTML =
        `<span class="badge-btn status-badge ${isClosed ? 'status-closed' : 'status-open'}" style="cursor:default">
            <span class="status-dot-inline"></span>${isClosed ? 'Closed' : 'Open'}
        </span>`;

    // Other fields
    const blocked = d.isBlocked === true;
    document.getElementById('cdBlocked').innerHTML = blocked
        ? '<span class="cell-blocked-tag">Blocked</span>'
        : 'No';
    document.getElementById('cdCreated').textContent = fmtDate(d.contactCreatedAt) || '—';
    document.getElementById('cdMsgCount').textContent = d.messageCount ?? 0;
    document.getElementById('cdNoteCount').textContent = d.noteCount ?? 0;
}
