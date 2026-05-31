/* i18n fallback — guarantee t() is callable even if i18n.js failed to load. */
if (typeof window.t !== 'function') {
    window.t = function (key) {
        var dict = window.__i18n__ || {};
        var value = dict[key] != null ? dict[key] : key;
        for (var i = 1; i < arguments.length; i++) {
            value = value.split('{' + (i - 1) + '}').join(String(arguments[i]));
        }
        return value;
    };
}

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

function stageName(id) {
    const s = STAGES[id] ?? STAGES[0];
    return t(s.key);
}

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
    const el = document.createElement('div');
    el.className = `toast toast-${type}`;
    el.textContent = message;
    stack.appendChild(el);
    setTimeout(() => {
        el.classList.add('toast-out');
        setTimeout(() => el.remove(), 200);
    }, 2400);
}

/* ─── Format helpers ──────────────────────────────────────────────── */
function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function fmtDateTime(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    return d.toLocaleString(window.__i18nCulture__ || undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function fmtDate(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    return d.toLocaleDateString(window.__i18nCulture__ || undefined, { day: 'numeric', month: 'short', year: 'numeric' });
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
    tbody.innerHTML = `<tr><td colspan="10" class="contacts-empty">${escapeHtml(t('Action.Loading'))}</td></tr>`;

    try {
        const resp = await fetch('/api/contacts?' + q.toString());
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        const result = await resp.json();
        renderRows(result.items);
        renderPagination(result);
        updateSortHeaders();
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="10" class="contacts-empty">${escapeHtml(t('Contacts.LoadFailed', e.message))}</td></tr>`;
    }
}

function renderRows(items) {
    const tbody = document.getElementById('contactsBody');
    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="10" class="contacts-empty">${escapeHtml(t('Contacts.NoMatch'))}</td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(c => renderRow(c)).join('');
}

/** Compact AI-agent chip rendered into the new column on each contact row. */
function renderAgentChip(c) {
    if (c.assignedAgentName) {
        const avatar = c.assignedAgentAvatarPath
            ? `<img src="${escapeHtml(c.assignedAgentAvatarPath)}" alt="" />`
            : '🤖';
        return `
            <button class="chat-agent-picker" type="button"
                    data-agent-picker="contact"
                    data-contact-id="${c.id}"
                    data-current-agent="${c.assignedAgentId}">
                <span class="chat-agent-picker-avatar">${avatar}</span>
                <span class="chat-agent-picker-name">${escapeHtml(c.assignedAgentName)}</span>
            </button>`;
    }
    return `
        <button class="chat-agent-picker" type="button"
                data-agent-picker="contact"
                data-contact-id="${c.id}"
                data-current-agent="">
            <span class="chat-agent-picker-avatar">🤖</span>
            <span class="chat-agent-picker-name" style="color: var(--text-muted);">${escapeHtml(t('Agents.Picker.Unassign'))}</span>
        </button>`;
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
        : `<span class="cell-muted">${escapeHtml(t('Contacts.NoMessages'))}</span>`;

    // Lifecycle — colored badge button
    const stage = STAGES[c.lifecycleStage] ?? STAGES[0];
    const lifecycleHtml = `
        <button class="badge-btn lc-badge lc-${stage.cls}" type="button"
                data-popover="lifecycle" data-id="${c.id}" data-current="${c.lifecycleStage}">
            <span class="badge-dot"></span>
            <span>${escapeHtml(t(stage.key))}</span>
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
                <span>${escapeHtml(t('Inbox.Unassigned'))}</span>
                <span class="chev">▾</span>
            </button>`;
    }

    // Status — green Open / grey Closed
    const isClosed = c.conversationStatus === 2;
    const statusCls = isClosed ? 'status-closed' : 'status-open';
    const statusLabel = isClosed ? t('Inbox.Closed') : t('Inbox.Open');
    const statusHtml = `
        <button class="badge-btn status-badge ${statusCls}" type="button"
                data-popover="status" data-id="${c.id}" data-current="${isClosed ? 2 : 0}">
            <span class="status-dot-inline"></span>
            <span>${escapeHtml(statusLabel)}</span>
            <span class="chev">▾</span>
        </button>`;

    // Country / Language with muted "Unknown" fallback
    const countryHtml = c.country ? escapeHtml(c.country) : muted(t('Contacts.Unknown'));
    const langHtml = `
        <input type="text" class="cell-lang-input"
               value="${escapeHtml(c.language || '')}"
               placeholder="${escapeHtml(t('Contacts.LangPlaceholder'))}"
               data-action="lang" data-id="${c.id}" />`;

    // Blocked tag inline next to name
    const blockedTag = c.isBlocked ? `<span class="cell-blocked-tag">${escapeHtml(t('Contacts.Blocked'))}</span>` : '';

    return `
    <tr data-id="${c.id}" data-conv-id="${c.primaryConversationId ?? ''}" data-instance-id="${c.primaryInstanceId ?? ''}" data-channel-type="${c.channelType ?? ''}" data-name="${escapeHtml(c.displayName || c.phoneNumber || '')}" class="${c.isBlocked ? 'is-blocked' : ''}">
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
        <td>${renderAgentChip(c)}</td>
        <td>${statusHtml}</td>
        <td class="cell-actions">
            <div class="action-menu-wrap">
                <button class="action-trigger" type="button" data-popover="actions" data-id="${c.id}" aria-label="${escapeHtml(t('Roles.Action.ActionsAria'))}">
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
        total === 0 ? t('Contacts.Pagination.NoContacts') : t('Contacts.Pagination.Range', from, to, total);
    document.getElementById('pageLabel').textContent = t('Contacts.Pagination.Page', page, totalPages);
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
            <span>${escapeHtml(t(s.key))}</span>
            ${s.id === currentId ? `<span class="cm-popover-check">${svgIcon('check', 12)}</span>` : ''}
        </button>`).join('');

    return `<div class="cm-popover cm-popover-md" data-anchor="${anchorId}" role="menu">${items}</div>`;
}

function buildStatusPopover(currentId, anchorId) {
    const items = [
        { id: 0, label: t('Inbox.Open'),   cls: 'status-open'   },
        { id: 2, label: t('Inbox.Closed'), cls: 'status-closed' }
    ].map(s => `
        <button class="cm-popover-item" data-status="${s.id}">
            <span class="status-dot-inline ${s.cls}"></span>
            <span>${escapeHtml(s.label)}</span>
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
              <span>${escapeHtml(t('Contacts.AssignToMe'))}</span>
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
            <span>${escapeHtml(t('Inbox.Unassigned'))}</span>
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
            ${svgIcon('eye', 14)}<span>${escapeHtml(t('Contacts.Action.ViewDetails'))}</span>
        </button>
        <button class="cm-popover-item" data-action="view-contact">
            ${svgIcon('user', 14)}<span>${escapeHtml(t('Contacts.Action.ViewContact'))}</span>
        </button>
        <div class="cm-popover-sep"></div>
        <button class="cm-popover-item" data-action="block">
            ${isBlocked ? svgIcon('shield', 14) : svgIcon('shield-off', 14)}
            <span>${escapeHtml(isBlocked ? t('Contacts.Action.Unblock') : t('Contacts.Action.Block'))}</span>
        </button>
        <button class="cm-popover-item is-danger" data-action="delete">
            ${svgIcon('trash', 14)}<span>${escapeHtml(t('Contacts.Action.Delete'))}</span>
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
            showToast(err.error || t('Contacts.Toast.LifecycleFailed'), 'error');
        } else {
            showToast(t('Contacts.Toast.LifecycleUpdated', stageName(stage)), 'success');
            loadContacts();
            // If the modal is open with this contact, repaint its stage live.
            if (_cdCtx.contactId === id) {
                paintCdLifecycle(stage);
                const trigger = document.getElementById('cdHeroStageDisplay');
                if (trigger) trigger.dataset.current = String(stage);
            }
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
            showToast(err.error || t('Contacts.Toast.StatusFailed'), 'error');
        } else {
            showToast(status === 2 ? t('Contacts.Toast.MarkedClosed') : t('Contacts.Toast.Reopened'), 'success');
            loadContacts();
            if (_cdCtx.contactId === id) paintCdStatus(status);
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
            showToast(err.error || t('Contacts.Toast.AssignFailed'), 'error');
        } else {
            showToast(userId ? t('Contacts.Toast.Assigned') : t('Contacts.Toast.Unassigned'), 'success');
            loadContacts();
            if (_cdCtx.contactId === id) {
                // Look up the display name from the team list we already have.
                const team = (window.__TEAM__ || []);
                const found = team.find(u => u.id === userId);
                paintCdAssigned(found ? found.name : null);
            }
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
            const contactId = row?.dataset.id;
            const convId    = row?.dataset.convId;
            const instId    = row?.dataset.instanceId;
            const chType    = row?.dataset.channelType;
            openContactDetails(convId, instId, chType, contactId);
            return;
        }

        if (action === 'view-contact') {
            // Deep-link to the chat when the contact has a conversation; otherwise (imported
            // contacts that haven't messaged yet) open the contact-details modal in-place so
            // the user still sees their info — clicking nothing is the worst UX.
            const contactId = row?.dataset.id;
            const convId    = row?.dataset.convId;
            const instId    = row?.dataset.instanceId;
            const chType    = row?.dataset.channelType;
            if (convId && instId) {
                window.location.href = `/dashboard/chats?instance=${instId}&conversation=${convId}`;
                return;
            }
            openContactDetails(null, null, chType, contactId);
            return;
        }

        if (action === 'block') {
            const isBlocked = row?.classList.contains('is-blocked') || false;
            const next = !isBlocked;
            const name = row?.querySelector('.cell-contact-name')?.textContent.trim() || t('Contacts.ThisContact');
            openConfirm({
                title: next ? t('Contacts.Confirm.Block.Title') : t('Contacts.Confirm.Unblock.Title'),
                body: `<strong>${escapeHtml(name)}</strong>`,
                bullets: next
                    ? [t('Contacts.Confirm.Block.Bullet1'), t('Contacts.Confirm.Block.Bullet2'), t('Contacts.Confirm.Block.Bullet3')]
                    : [t('Contacts.Confirm.Unblock.Bullet1'), t('Contacts.Confirm.Unblock.Bullet2')],
                confirmLabel: next ? t('Contacts.Confirm.Block.Action') : t('Contacts.Confirm.Unblock.Action'),
                onConfirm: async () => {
                    const resp = await postJson(`/api/contacts/${id}/block`, { blocked: next });
                    if (!resp.ok) {
                        const err = await resp.json().catch(() => ({}));
                        showToast(err.error || t('Contacts.Toast.BlockFailed'), 'error');
                        return;
                    }
                    showToast(next ? t('Contacts.Toast.Blocked') : t('Contacts.Toast.Unblocked'), 'success');
                    loadContacts();
                }
            });
        }

        if (action === 'delete') {
            const name = row?.querySelector('.cell-contact-name')?.textContent.trim() || t('Contacts.ThisContact');
            openConfirm({
                title: t('Contacts.Confirm.Delete.Title'),
                body: `<strong>${escapeHtml(name)}</strong>`,
                bullets: [t('Contacts.Confirm.Delete.Bullet1'), t('Contacts.Confirm.Delete.Bullet2')],
                confirmLabel: t('Contacts.Confirm.Delete.Action'),
                isDanger: true,
                onConfirm: async () => {
                    const resp = await fetch(`/api/contacts/${id}`, {
                        method: 'DELETE',
                        headers: { 'RequestVerificationToken': token() }
                    });
                    if (!resp.ok) {
                        const err = await resp.json().catch(() => ({}));
                        showToast(err.error || t('Contacts.Toast.DeleteFailed'), 'error');
                        return;
                    }
                    showToast(t('Contacts.Toast.Deleted'), 'success');
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
            showToast(err.error || t('Contacts.Toast.LangFailed'), 'error');
            return;
        }
        showToast(t('Contacts.Toast.LangUpdated'), 'success');
    } catch (err) {
        showToast(t('Toast.NetworkError', err.message), 'error');
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
    btn.textContent = confirmLabel || t('Common.Confirm.Action');
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
    let timer;
    return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), ms); };
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
    label.textContent = t('Contacts.Exporting');

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
        showToast(t('Contacts.Toast.ExportReady'), 'success');
    } catch (e) {
        showToast(t('Contacts.Toast.ExportFailed', e.message), 'error');
    } finally {
        btn.disabled = false;
        spinner.classList.add('d-none');
        label.textContent = t('Contacts.Export');
    }
});

/* ─── Boot ──────────────────────────────────────────────────────────── */
loadContacts();

/* ─── Contact Details modal (Lead Card Detail) ────────────────────── */
// Stashed per-open so populateDetails() can use what the row already knows
// (channel type) and other handlers know what conversation we're editing.
const _cdCtx = { conversationId: null, instanceId: null, channelType: null, contactId: null };

function openContactDetails(conversationId, instanceId, channelType, contactId) {
    const modal = document.getElementById('contactDetailsModal');
    if (!modal) return;

    _cdCtx.conversationId = conversationId ? parseInt(conversationId, 10) : null;
    _cdCtx.instanceId     = instanceId     ? parseInt(instanceId, 10)     : null;
    _cdCtx.channelType    = (channelType === '' || channelType == null) ? null : parseInt(channelType, 10);
    _cdCtx.contactId      = contactId      ? parseInt(contactId, 10)      : null;

    document.getElementById('cdLoading').classList.remove('d-none');
    document.getElementById('cdLoading').textContent = t('Action.Loading');
    document.getElementById('cdContent').classList.add('d-none');
    modal.classList.remove('d-none');

    // When we have a conversation, the chat endpoint is richer (it joins messages, tags, etc).
    // Otherwise — e.g. a contact imported from Excel that hasn't messaged yet — fall back to
    // the contact-only endpoint so the modal still works.
    const fetchPromise = conversationId
        ? fetch(`/dashboard/chats/${conversationId}/contact`)
        : (_cdCtx.contactId
            ? fetch(`/api/contacts/${_cdCtx.contactId}/details`)
            : Promise.reject(new Error('missing-context')));

    if (!conversationId && !_cdCtx.contactId) {
        document.getElementById('cdLoading').textContent = t('Contacts.NoPrimaryConversation');
        return;
    }

    fetchPromise
        .then(r => r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)))
        .then(d => {
            // populateDetails wires the "Open conversation" link off d.conversationId/_cdCtx.instanceId
            // — paintCdOpenConversation handles the no-conversation case below.
            populateDetails(d);
            paintCdOpenConversation(d);
            loadContactFiles(d.contactId || _cdCtx.contactId);
        })
        .catch(err => {
            document.getElementById('cdLoading').textContent = t('Contacts.LoadFailed', err.message);
        });
}

/** Toggle the hero "Open Conversation" button + Conversations tab between linked and disabled
 *  depending on whether the contact actually has a conversation. */
function paintCdOpenConversation(d) {
    const openLink = document.getElementById('cdOpenChat');
    const tabConv  = document.getElementById('cdTabConversations');
    const hasConv  = !!(d && d.conversationId);
    // Prefer the row-known instanceId (more reliable than what the server-trimmed DTO carries).
    const instId   = _cdCtx.instanceId || null;

    if (openLink) {
        if (hasConv) {
            openLink.href = instId
                ? `/dashboard/chats?instance=${instId}&conversation=${d.conversationId}`
                : `/dashboard/chats?conversation=${d.conversationId}`;
            openLink.removeAttribute('aria-disabled');
            openLink.classList.remove('is-disabled');
            openLink.title = '';
        } else {
            openLink.href = '#';
            openLink.setAttribute('aria-disabled', 'true');
            openLink.classList.add('is-disabled');
            openLink.title = t('Contacts.NoPrimaryConversation');
            openLink.onclick = (e) => { e.preventDefault(); showToast(t('Contacts.NoPrimaryConversation'), 'info'); };
        }
    }
    if (tabConv) {
        tabConv.classList.toggle('is-disabled', !hasConv);
        if (!hasConv) tabConv.title = t('Contacts.NoPrimaryConversation');
        else tabConv.title = '';
    }
}

function closeContactDetailsModal() {
    document.getElementById('contactDetailsModal')?.classList.add('d-none');
    // Trigger a refresh of the underlying table so any edits made inside the
    // modal (lifecycle / status / assignee / language / name) are visible.
    if (typeof loadContacts === 'function') loadContacts();
}
window.closeContactDetailsModal = closeContactDetailsModal;

/* ─── Internal (private) contact files ──────────────────────────────────
   These files are team-only: stored in private server storage and reachable
   only through the authorized /api/contacts/{id}/files endpoints. The contact
   never sees or receives them. */
const _canEditContacts = () => window.__canEditContacts__ === true || window.__canEditContacts__ === 'true';
let _cdFilesContactId = null;

function fmtFileSize(bytes) {
    if (bytes == null) return '';
    if (bytes < 1024) return bytes + ' B';
    const units = ['KB', 'MB', 'GB'];
    let v = bytes / 1024, i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return (v < 10 ? v.toFixed(1) : Math.round(v)) + ' ' + units[i];
}

function fileExtBadge(name) {
    const ext = (name || '').split('.').pop();
    return (ext && ext !== name ? ext : 'file').toUpperCase().slice(0, 4);
}

async function loadContactFiles(contactId) {
    _cdFilesContactId = contactId || null;
    const card = document.getElementById('cdFilesCard');
    if (!card) return;

    // Hide the upload control for users without contacts.edit (server enforces this too).
    const upload = document.getElementById('cdFilesUpload');
    if (upload) upload.classList.toggle('d-none', !_canEditContacts());

    const list = document.getElementById('cdFilesList');
    const empty = document.getElementById('cdFilesEmpty');
    if (!_cdFilesContactId) { card.classList.add('d-none'); return; }
    card.classList.remove('d-none');
    list.innerHTML = `<div class="cd-files-loading">${escapeHtml(t('Action.Loading'))}</div>`;
    empty.classList.add('d-none');

    try {
        const res = await fetch(`/api/contacts/${_cdFilesContactId}/files`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const files = await res.json();
        renderContactFiles(files);
    } catch (e) {
        list.innerHTML = `<div class="cd-files-loading">${escapeHtml(t('ContactFiles.LoadError'))}</div>`;
    }
}

function renderContactFiles(files) {
    const list = document.getElementById('cdFilesList');
    const empty = document.getElementById('cdFilesEmpty');
    if (!list) return;

    if (!files || files.length === 0) {
        list.innerHTML = '';
        empty.classList.remove('d-none');
        return;
    }
    empty.classList.add('d-none');

    const canEdit = _canEditContacts();
    const cid = _cdFilesContactId;
    list.innerHTML = files.map(f => {
        const dl = `/api/contacts/${cid}/files/${f.id}/download`;
        const sub = [escapeHtml(fmtFileSize(f.sizeBytes)), escapeHtml(f.uploadedByName || ''), escapeHtml(fmtDateTime(f.uploadedAtUtc) || '')]
            .filter(Boolean).join(' · ');
        const editBtns = canEdit ? `
            <button type="button" class="cd-file-btn" title="${escapeHtml(t('ContactFiles.Rename'))}" data-file-rename="${f.id}" data-file-name="${escapeHtml(f.fileName)}">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            </button>
            <button type="button" class="cd-file-btn cd-file-btn-danger" title="${escapeHtml(t('ContactFiles.Delete'))}" data-file-delete="${f.id}" data-file-name="${escapeHtml(f.fileName)}">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/></svg>
            </button>` : '';
        return `<div class="cd-file-row">
            <span class="cd-file-ext" aria-hidden="true">${escapeHtml(fileExtBadge(f.fileName))}</span>
            <div class="cd-file-main">
                <a class="cd-file-name" href="${dl}?inline=1" target="_blank" rel="noopener" title="${escapeHtml(t('ContactFiles.View'))}">${escapeHtml(f.fileName)}</a>
                <span class="cd-file-sub">${sub}</span>
            </div>
            <div class="cd-file-actions">
                <a class="cd-file-btn" href="${dl}" title="${escapeHtml(t('ContactFiles.Download'))}" download>
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                </a>
                ${editBtns}
            </div>
        </div>`;
    }).join('');
}

async function uploadContactFiles(fileList) {
    if (!_cdFilesContactId || !fileList || fileList.length === 0) return;
    const fd = new FormData();
    for (const f of fileList) fd.append('files', f);

    try {
        const res = await fetch(`/api/contacts/${_cdFilesContactId}/files`, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token() },
            body: fd
        });
        if (res.status === 403) { showToast(t('ContactFiles.Forbidden'), 'error'); return; }
        if (!res.ok) { showToast(t('ContactFiles.UploadError'), 'error'); return; }
        const data = await res.json();
        if (data.errors && data.errors.length) {
            data.errors.forEach(er => showToast(`${er.file}: ${er.error}`, 'error'));
        }
        if (data.uploaded && data.uploaded.length) {
            showToast(t('ContactFiles.Uploaded', data.uploaded.length), 'success');
        }
        loadContactFiles(_cdFilesContactId);
    } catch (e) {
        showToast(t('ContactFiles.UploadError'), 'error');
    }
}

async function renameContactFile(fileId, currentName) {
    const next = window.prompt(t('ContactFiles.RenamePrompt'), currentName || '');
    if (next == null) return;
    const name = next.trim();
    if (!name || name === currentName) return;
    try {
        const res = await fetch(`/api/contacts/${_cdFilesContactId}/files/${fileId}/rename`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify({ name })
        });
        if (!res.ok) { showToast(t('ContactFiles.RenameError'), 'error'); return; }
        loadContactFiles(_cdFilesContactId);
    } catch (e) {
        showToast(t('ContactFiles.RenameError'), 'error');
    }
}

async function deleteContactFile(fileId, name) {
    if (!window.confirm(t('ContactFiles.DeleteConfirm', name || ''))) return;
    try {
        const res = await fetch(`/api/contacts/${_cdFilesContactId}/files/${fileId}`, {
            method: 'DELETE',
            headers: { 'RequestVerificationToken': token() }
        });
        if (!res.ok) { showToast(t('ContactFiles.DeleteError'), 'error'); return; }
        showToast(t('ContactFiles.Deleted'), 'success');
        loadContactFiles(_cdFilesContactId);
    } catch (e) {
        showToast(t('ContactFiles.DeleteError'), 'error');
    }
}

// Wire the upload button + delegated row actions once. The modal markup is always in the DOM.
document.addEventListener('DOMContentLoaded', () => {
    const btn = document.getElementById('cdFilesUploadBtn');
    const input = document.getElementById('cdFilesInput');
    if (btn && input) {
        btn.addEventListener('click', () => input.click());
        input.addEventListener('change', () => {
            uploadContactFiles(input.files);
            input.value = ''; // allow re-selecting the same file
        });
    }

    const list = document.getElementById('cdFilesList');
    if (list) {
        list.addEventListener('click', (e) => {
            const ren = e.target.closest('[data-file-rename]');
            if (ren) { renameContactFile(parseInt(ren.dataset.fileRename, 10), ren.dataset.fileName); return; }
            const del = e.target.closest('[data-file-delete]');
            if (del) { deleteContactFile(parseInt(del.dataset.fileDelete, 10), del.dataset.fileName); return; }
        });
    }
});

/* Update the cd-stages bar to highlight a given stage. */
function paintCdStages(stage) {
    const stagesBar = document.getElementById('cdStages');
    if (!stagesBar) return;
    stagesBar.querySelectorAll('.cd-stage').forEach(el => {
        const idx = parseInt(el.getAttribute('data-stage'), 10);
        el.classList.remove('is-active', 'is-done');
        if (idx < stage) el.classList.add('is-done');
        else if (idx === stage) el.classList.add('is-active');
    });
}

/* Repaint the hero stage pill + the side-column lifecycle badge. */
function paintCdLifecycle(stage) {
    const s = STAGES[stage] ?? STAGES[0];
    const heroText = document.getElementById('cdHeroStageText');
    if (heroText) heroText.textContent = t(s.key);
    const pill = document.getElementById('cdLifecycle');
    if (pill) {
        pill.className = 'cd-pill';
        pill.innerHTML = `<span class="badge-btn lc-${s.cls}" style="cursor:default"><span class="badge-dot"></span>${escapeHtml(t(s.key))}</span>`;
    }
    paintCdStages(stage);
}

/* Repaint the status badge (Open / Closed). */
function paintCdStatus(status) {
    const isClosed = status === 2;
    const el = document.getElementById('cdStatus');
    if (!el) return;
    el.innerHTML =
        `<button type="button" class="badge-btn status-badge ${isClosed ? 'status-closed' : 'status-open'} cd-status-toggle"
                title="${escapeHtml(isClosed ? t('Contacts.Toast.Reopened') : t('Contacts.Toast.MarkedClosed'))}">
            <span class="status-dot-inline"></span>${escapeHtml(isClosed ? t('Inbox.Closed') : t('Inbox.Open'))}
        </button>`;
}

/* Repaint the assigned-user value as a popover trigger so click opens the team picker. */
function paintCdAssigned(name) {
    const el = document.getElementById('cdAssigned');
    if (!el) return;
    const id = _cdCtx.contactId;
    // Look up current userId from the team list if we have a match by name.
    const team = (window.__TEAM__ || []);
    const matched = team.find(u => u.name === name);
    const currentId = matched ? matched.id : '';
    const inner = name
        ? `<span class="cd-assignee"><span class="assignee-avatar">${escapeHtml(avatarInitials(name))}</span>${escapeHtml(name)}</span>`
        : `<span class="cell-muted">${escapeHtml(t('Inbox.Unassigned'))}</span>`;
    el.innerHTML = id
        ? `<button type="button" class="cd-assigned-btn" data-popover="assignee" data-id="${id}" data-current="${escapeHtml(currentId)}">
              ${inner}
              <span class="chev">▾</span>
           </button>`
        : inner;
}

function populateDetails(d) {
    document.getElementById('cdLoading').classList.add('d-none');
    document.getElementById('cdContent').classList.remove('d-none');

    // Stash the contactId so the wired controls can call /api/contacts/{id}/...
    _cdCtx.contactId = d.contactId;

    // ── Hero: avatar (image OR initials) ──
    const initials = avatarInitials(d.displayName || d.phoneNumber);
    const avatarEl = document.getElementById('cdAvatar');
    if (d.avatarUrl) {
        avatarEl.innerHTML = `<img src="${escapeHtml(d.avatarUrl)}" alt="">`;
    } else {
        avatarEl.textContent = initials;
    }

    // Name — static display (no rename API on the backend).
    document.getElementById('cdName').textContent = d.displayName || d.phoneNumber || '—';

    // Company / channel line under the name (display only — derived from instance).
    document.getElementById('cdChannel').textContent = d.instanceDisplayName || '—';

    // Channel chip uses the row's known channelType, falling back to instance name.
    renderChannelChip(d);

    // Lifecycle pill + hero stage display (wire it as a popover trigger).
    paintCdLifecycle(d.lifecycleStage ?? 0);
    const heroStage = document.getElementById('cdHeroStageDisplay');
    if (heroStage && d.contactId) {
        heroStage.setAttribute('data-popover', 'lifecycle');
        heroStage.setAttribute('data-id', String(d.contactId));
        heroStage.setAttribute('data-current', String(d.lifecycleStage ?? 0));
    }

    // ── Contact-info column rows ──
    document.getElementById('cdPhone').textContent = d.phoneNumber ? '+' + d.phoneNumber : '—';
    document.getElementById('cdCountry').innerHTML = d.country
        ? escapeHtml(d.country)
        : `<span class="cell-muted">${escapeHtml(t('Contacts.Unknown'))}</span>`;

    // Language row → inline editor (click-to-edit, posts to /api/contacts/{id}/language).
    const langEl = document.getElementById('cdLanguage');
    langEl.innerHTML = d.language
        ? `<span class="cd-editable" id="cdLangText">${escapeHtml(d.language)}</span>`
        : `<span class="cell-muted cd-editable" id="cdLangText">${escapeHtml(t('Contacts.Unknown'))}</span>`;
    const langText = document.getElementById('cdLangText');
    langText.title = 'Click to edit';
    langText.onclick = () => startInlineEdit(langText, async (next) => {
        const id = _cdCtx.contactId; if (!id) return false;
        const resp = await postJson(`/api/contacts/${id}/language`, { contactId: id, language: next });
        if (resp.ok) { showToast('Updated', 'success'); return true; }
        showToast('Update failed', 'error'); return false;
    });

    // Assignee row (click → popover with team list)
    paintCdAssigned(d.assignedUserName);

    // Status (button — click to toggle Open/Closed)
    paintCdStatus(d.conversationStatus ?? 0);

    // Blocked
    const blocked = d.isBlocked === true;
    document.getElementById('cdBlocked').innerHTML = blocked
        ? `<span class="cell-blocked-tag">${escapeHtml(t('Contacts.Blocked'))}</span>`
        : escapeHtml(t('Contacts.NotBlocked'));

    // ── Engagement counters ──
    const created = fmtDate(d.contactCreatedAt) || '—';
    document.getElementById('cdCreated').textContent = created;
    document.getElementById('cdMsgCount').textContent = d.messageCount ?? 0;
    document.getElementById('cdNoteCount').textContent = d.noteCount ?? 0;
    const heroCreated = document.getElementById('cdHeroCreated');
    const heroMsgVal  = document.getElementById('cdHeroMsgValue');
    if (heroCreated) heroCreated.textContent = created;
    if (heroMsgVal)  heroMsgVal.textContent  = d.messageCount ?? 0;

    // ── Tags row ──
    const tagsHost = document.getElementById('cdTags');
    if (tagsHost) {
        const tags = Array.isArray(d.tags) ? d.tags : [];
        if (tags.length === 0) {
            tagsHost.innerHTML = `<span class="cd-tag-empty">${escapeHtml(t('Contacts.Unknown'))}</span>`;
        } else {
            tagsHost.innerHTML = tags.map(tag => {
                const color = tag.color || '#aabba4';
                return `<span class="cd-tag" style="border-color:${color}33;color:${color}">${escapeHtml(tag.name || '')}</span>`;
            }).join('');
        }
    }

    // ── Activity timeline — seeded from the existing counts/dates. ──
    renderCdTimeline(d);

    // Tabs: route "Conversations" to the chat link.
    const tabConv = document.getElementById('cdTabConversations');
    const openLink = document.getElementById('cdOpenChat');
    if (tabConv && openLink) {
        tabConv.onclick = (e) => { e.preventDefault(); if (openLink.href) window.location.href = openLink.href; };
    }
}

/* Channel chip in the hero — uses the channelType we stashed when the modal
   was opened (falls back to inferring from the instance display name). */
function renderChannelChip(d) {
    const chipsHost = document.getElementById('cdChannelChips');
    if (!chipsHost) return;

    const CHANNEL_PALETTE = [
        { name: 'WhatsApp',  bg: '#25d366', letter: 'W' },
        { name: 'Instagram', bg: 'linear-gradient(135deg,#feda77,#d6249f,#4f5bd5)', letter: 'I' },
        { name: 'Messenger', bg: '#0084ff', letter: 'M' },
        { name: 'Telegram',  bg: '#229ed9', letter: 'T' }
    ];
    let pick = null;
    if (_cdCtx.channelType != null && CHANNEL_PALETTE[_cdCtx.channelType]) {
        pick = CHANNEL_PALETTE[_cdCtx.channelType];
    } else {
        const ch = (d.instanceDisplayName || '').toLowerCase();
        if      (ch.includes('whatsapp'))  pick = CHANNEL_PALETTE[0];
        else if (ch.includes('instagram')) pick = CHANNEL_PALETTE[1];
        else if (ch.includes('messenger') || ch.includes('facebook')) pick = CHANNEL_PALETTE[2];
        else if (ch.includes('telegram'))  pick = CHANNEL_PALETTE[3];
    }
    if (!pick) pick = { name: d.instanceDisplayName || '—', bg: '#6f8a64', letter: (d.instanceDisplayName || '?')[0].toUpperCase() };

    chipsHost.innerHTML = `
      <span class="cd-hero-chip" title="${escapeHtml(d.instanceDisplayName || '')}">
        <span class="cd-hero-chip-dot" style="background:${pick.bg}">${escapeHtml(pick.letter)}</span>
        <span>${escapeHtml(pick.name)}</span>
        <svg class="cd-hero-chip-check" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"/></svg>
      </span>`;
}

/* Activity timeline (right column) — seeded from real data we have today. */
function renderCdTimeline(d) {
    const timeline = document.getElementById('cdTimeline');
    if (!timeline) return;

    const iconSvg = (kind) => {
        if (kind === 'message') return '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>';
        if (kind === 'note')    return '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
        return '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="16"/><line x1="8" y1="12" x2="16" y2="12"/></svg>';
    };

    const items = [];
    if (d.lastIncomingAtUtc) {
        items.push({ icon: 'message', title: t('Inbox.Title'), time: fmtDate(d.lastIncomingAtUtc), sub: d.displayName || d.phoneNumber || '' });
    }
    if (d.messageCount > 0) {
        items.push({ icon: 'message', title: `${d.messageCount} ${t('Contacts.TotalMessages').toLowerCase()}`, time: '', sub: d.instanceDisplayName || '' });
    }
    if (d.noteCount > 0) {
        items.push({ icon: 'note', title: `${d.noteCount} ${t('Contacts.InternalNotes').toLowerCase()}`, time: '', sub: '' });
    }
    if (d.contactCreatedAt) {
        items.push({ icon: 'plus', title: t('Dashboard.Activity.Title'), time: fmtDate(d.contactCreatedAt), sub: d.displayName || d.phoneNumber || '' });
    }

    timeline.innerHTML = items.length === 0
        ? `<li class="cd-timeline-empty">${escapeHtml(t('Dashboard.Attention.EmptyTitle'))}</li>`
        : items.map(item => `
            <li class="cd-timeline-item">
                <span class="cd-timeline-icon">${iconSvg(item.icon)}</span>
                <div class="cd-timeline-body">
                    <div class="cd-timeline-title">${escapeHtml(item.title)}</div>
                    ${item.time ? `<div class="cd-timeline-time">${escapeHtml(item.time)}</div>` : ''}
                    ${item.sub  ? `<div class="cd-timeline-sub">${escapeHtml(item.sub)}</div>`  : ''}
                </div>
            </li>`).join('');
}

/* ─── Inline-edit helper (used by name + language) ─────────────────────
   Replaces the target element with a text input, restores it on Esc/blur.
   onSave(value) returns a promise<boolean>; on success the new value sticks. */
function startInlineEdit(el, onSave) {
    if (el.dataset.editing === '1') return;
    el.dataset.editing = '1';
    const original = el.textContent;
    const input = document.createElement('input');
    input.type = 'text';
    input.value = original.trim() === '—' || el.classList.contains('cell-muted') ? '' : original.trim();
    input.className = 'cd-inline-input';
    el.replaceWith(input);
    input.focus();
    input.select();

    const finish = async (commit) => {
        if (input._done) return;
        input._done = true;
        if (commit && input.value.trim() && input.value.trim() !== original.trim()) {
            const ok = await onSave(input.value.trim());
            if (ok) el.textContent = input.value.trim();
        }
        // Restore the original element (with whatever textContent it now has).
        el.dataset.editing = '0';
        input.replaceWith(el);
    };

    input.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter') { ev.preventDefault(); finish(true); }
        if (ev.key === 'Escape') { ev.preventDefault(); finish(false); }
    });
    input.addEventListener('blur', () => finish(true));
}

/* ─── Modal-scoped event wiring (status toggle, kebab, note submit) ───── */
document.addEventListener('click', async (e) => {
    // STATUS TOGGLE — click the Open/Closed pill to flip it.
    const statusBtn = e.target.closest('.cd-status-toggle');
    if (statusBtn) {
        const id = _cdCtx.contactId;
        if (!id) return;
        const isClosed = statusBtn.classList.contains('status-closed');
        const next = isClosed ? 0 : 2;
        const resp = await postJson(`/api/contacts/${id}/status`, { status: next });
        if (resp.ok) {
            showToast(next === 2 ? t('Contacts.Toast.MarkedClosed') : t('Contacts.Toast.Reopened'), 'success');
            paintCdStatus(next);
            if (typeof loadContacts === 'function') loadContacts();
        } else {
            showToast(t('Contacts.Toast.StatusFailed'), 'error');
        }
        return;
    }

    // KEBAB toggle
    const kebabBtn = e.target.closest('#cdKebabBtn');
    if (kebabBtn) {
        const menu = document.getElementById('cdKebabMenu');
        if (menu) menu.classList.toggle('d-none');
        return;
    }

    // KEBAB → Block / Unblock
    const blockItem = e.target.closest('#cdKebabBlock');
    if (blockItem) {
        document.getElementById('cdKebabMenu')?.classList.add('d-none');
        const id = _cdCtx.contactId; if (!id) return;
        const blockedTagEl = document.querySelector('#cdBlocked .cell-blocked-tag');
        const isBlocked = !!blockedTagEl;
        const next = !isBlocked;
        const resp = await postJson(`/api/contacts/${id}/block`, { blocked: next });
        if (resp.ok) {
            showToast(next ? t('Contacts.Toast.Blocked') : t('Contacts.Toast.Unblocked'), 'success');
            document.getElementById('cdBlocked').innerHTML = next
                ? `<span class="cell-blocked-tag">${escapeHtml(t('Contacts.Blocked'))}</span>`
                : escapeHtml(t('Contacts.NotBlocked'));
            const lbl = document.getElementById('cdKebabBlockLabel');
            if (lbl) lbl.textContent = next ? t('Contacts.Confirm.Unblock.Action') : t('Contacts.Confirm.Block.Action');
            if (typeof loadContacts === 'function') loadContacts();
        } else {
            showToast(t('Contacts.Toast.BlockFailed'), 'error');
        }
        return;
    }

    // KEBAB → Delete (uses the page's existing confirm modal)
    const delItem = e.target.closest('#cdKebabDelete');
    if (delItem) {
        document.getElementById('cdKebabMenu')?.classList.add('d-none');
        const id = _cdCtx.contactId; if (!id) return;
        const name = document.getElementById('cdName')?.textContent?.trim() || t('Contacts.ThisContact');
        openConfirm({
            title: t('Contacts.Confirm.Delete.Title'),
            body: `<strong>${escapeHtml(name)}</strong>`,
            bullets: [t('Contacts.Confirm.Delete.Bullet1'), t('Contacts.Confirm.Delete.Bullet2')],
            confirmLabel: t('Contacts.Confirm.Delete.Action'),
            isDanger: true,
            onConfirm: async () => {
                const resp = await fetch(`/api/contacts/${id}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': token() }
                });
                if (resp.ok) {
                    showToast(t('Contacts.Toast.Deleted'), 'success');
                    closeContactDetailsModal();
                } else {
                    showToast(t('Contacts.Toast.DeleteFailed'), 'error');
                }
            }
        });
        return;
    }

    // Close kebab when clicking outside it
    if (!e.target.closest('.cd-kebab-wrap')) {
        document.getElementById('cdKebabMenu')?.classList.add('d-none');
    }
});

/* Add-note form (POST /dashboard/chats/note) */
document.addEventListener('submit', async (e) => {
    const form = e.target.closest('#cdNoteForm');
    if (!form) return;
    e.preventDefault();

    const convId = _cdCtx.conversationId;
    const input  = document.getElementById('cdNoteInput');
    const feedback = document.getElementById('cdNoteFeedback');
    const submit = document.getElementById('cdNoteSubmit');
    const body = (input?.value || '').trim();
    if (!convId || !body) {
        if (feedback) feedback.textContent = t('Compose.Note') + '…';
        return;
    }
    submit.disabled = true;
    try {
        const resp = await postJson('/dashboard/chats/note', { conversationId: convId, body });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed', 'error');
            return;
        }
        showToast(t('Contacts.Toast.Updated') || 'Note added', 'success');
        if (input) input.value = '';
        // Bump note count + add a new timeline entry locally.
        const noteEl = document.getElementById('cdNoteCount');
        if (noteEl) noteEl.textContent = String((parseInt(noteEl.textContent, 10) || 0) + 1);
        const timeline = document.getElementById('cdTimeline');
        if (timeline) {
            const li = document.createElement('li');
            li.className = 'cd-timeline-item';
            li.innerHTML = `
                <span class="cd-timeline-icon"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg></span>
                <div class="cd-timeline-body">
                    <div class="cd-timeline-title">${escapeHtml(t('Compose.Note'))}</div>
                    <div class="cd-timeline-time">${escapeHtml('just now')}</div>
                    <div class="cd-timeline-sub">${escapeHtml(body.length > 80 ? body.slice(0, 77) + '…' : body)}</div>
                </div>`;
            // Insert at the top of the timeline (most-recent first).
            if (timeline.firstChild?.classList?.contains('cd-timeline-empty')) {
                timeline.innerHTML = '';
            }
            timeline.insertBefore(li, timeline.firstChild);
        }
    } finally {
        submit.disabled = false;
    }
});
