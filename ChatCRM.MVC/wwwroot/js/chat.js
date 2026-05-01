/* ─── State ─────────────────────────────────────────────────────────── */
let activeConversationId = null;
let atBottom = true;
const activeInstanceId = parseInt(document.getElementById('chatSidebar')?.dataset.activeInstance || '0', 10);

/* ─── SignalR ───────────────────────────────────────────────────────── */
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .withAutomaticReconnect()
    .build();

connection.on('ReceiveMessage', (data) => {
    const { conversationId, instanceId, message, unreadCount, instanceUnread } = data;

    // Belt-and-suspenders — server groups should already filter to active instance.
    if (instanceId && activeInstanceId && instanceId !== activeInstanceId) return;

    const exists = document.querySelector(`.conv-item[data-id="${conversationId}"]`);
    if (!exists) {
        // Brand new conversation within this instance — refresh so the server-rendered sidebar picks it up
        location.reload();
        return;
    }

    const isActive = conversationId === activeConversationId;

    // If the user is currently looking at this conversation, the badge stays at 0 — don't flash 1→0.
    updateSidebarRow(conversationId, message, isActive ? 0 : unreadCount);

    // Reflect the new instance-wide unread total on the dropdown row.
    if (instanceId) updateInstanceDropdownUnread(instanceId, isActive ? Math.max(0, instanceUnread - unreadCount) : instanceUnread);

    if (isActive) {
        appendMessage(message);
        if (atBottom) scrollToBottom();
        // Tell the server we've read it (server will broadcast ConversationRead to all tabs).
        fetch(`/dashboard/chats/${conversationId}/messages`);
    }
});

connection.on('ConversationRead', ({ conversationId, instanceId, instanceUnread }) => {
    updateBadge(conversationId, 0);
    if (instanceId) updateInstanceDropdownUnread(instanceId, instanceUnread);
});

connection.on('InstanceStatusChanged', ({ id, status }) => {
    if (id === activeInstanceId) {
        location.reload();
    }
});

// Remote-tab sync: another tab/user changed something on this conversation.
connection.on('ConversationAssigned', ({ conversationId, assignedUserId }) => {
    if (conversationId === activeConversationId) loadContactDetails(conversationId);
});

connection.on('ConversationStatusChanged', ({ conversationId, status }) => {
    if (conversationId === activeConversationId) {
        applyStatusToHeader(status);
        loadContactDetails(conversationId);
    }
});

connection.on('LifecycleStageChanged', ({ conversationId, stage }) => {
    if (conversationId === activeConversationId) applyLifecycleToHeader(stage);
});

connection.start().then(async () => {
    if (activeInstanceId > 0) {
        try { await connection.invoke('JoinInstance', activeInstanceId); }
        catch (err) { console.error('JoinInstance failed:', err); }
    }

    // Deep-link: `?conversation=N` auto-selects the conversation when the page loads.
    const params = new URLSearchParams(window.location.search);
    const wantedConv = parseInt(params.get('conversation') || '0', 10);
    if (wantedConv > 0) {
        // Defer slightly so the SignalR hello, sidebar render, and listeners are all ready.
        setTimeout(() => {
            const item = document.querySelector(`.conv-item[data-id="${wantedConv}"]`);
            if (item) selectConversation(wantedConv);
        }, 50);
    }
}).catch(err => console.error('SignalR error:', err));

connection.onreconnected(async () => {
    if (activeInstanceId > 0) {
        try { await connection.invoke('JoinInstance', activeInstanceId); } catch { /* ignore */ }
    }
});

/* ─── Instance dropdown (sidebar) ────────────────────────────────── */
function toggleInstanceMenu() {
    document.getElementById('instanceMenu').classList.toggle('d-none');
}

document.addEventListener('click', (e) => {
    const wrapper = document.querySelector('.instance-select-wrapper');
    if (wrapper && !wrapper.contains(e.target)) {
        document.getElementById('instanceMenu')?.classList.add('d-none');
    }
});

/* ─── Conversation selection ────────────────────────────────────────── */
let composeMode = 'reply';   // 'reply' | 'note'
let contactDetails = null;

function selectConversation(id) {
    if (activeConversationId === id) return;

    document.querySelectorAll('.conv-item').forEach(el => el.classList.remove('active'));
    const item = document.querySelector(`.conv-item[data-id="${id}"]`);
    if (item) item.classList.add('active');

    activeConversationId = id;

    document.getElementById('chatEmpty').classList.add('d-none');
    document.getElementById('chatWindow').classList.remove('d-none');

    const phone = item?.dataset.phone ?? '';
    const name = item?.querySelector('.conv-name')?.textContent.trim() ?? phone;
    const avatarText = name.charAt(0).toUpperCase();

    document.getElementById('chatHeaderName').textContent = name;
    document.getElementById('chatHeaderPhone').textContent = phone;
    document.getElementById('chatHeaderAvatar').textContent = avatarText;

    loadMessages(id);
    loadContactDetails(id);
    updateBadge(id, 0);

    setComposeMode('reply');

    if (window.innerWidth <= 768) {
        document.getElementById('chatMain')?.classList.add('show');
    }
}

/* ─── Compose mode (reply vs note) ──────────────────────────────────── */
function setComposeMode(mode) {
    composeMode = mode;
    document.querySelectorAll('.compose-mode-btn').forEach(b => {
        b.classList.toggle('active', b.dataset.mode === mode);
    });
    const bar = document.querySelector('.chat-input-bar');
    const input = document.getElementById('messageInput');
    if (mode === 'note') {
        bar?.classList.add('note-mode');
        if (input) input.placeholder = 'Add an internal note (only visible to your team)…';
    } else {
        bar?.classList.remove('note-mode');
        if (input) input.placeholder = 'Type a message…';
    }
}

/* ─── Header dropdowns: assign + status ─────────────────────────────── */
function toggleAssign() {
    document.getElementById('assignMenu')?.classList.toggle('d-none');
    document.getElementById('statusMenu')?.classList.add('d-none');
}

function toggleStatus() {
    document.getElementById('statusMenu')?.classList.toggle('d-none');
    document.getElementById('assignMenu')?.classList.add('d-none');
}

document.addEventListener('click', (e) => {
    if (!e.target.closest('#assignWrap')) document.getElementById('assignMenu')?.classList.add('d-none');
    if (!e.target.closest('#statusWrap')) document.getElementById('statusMenu')?.classList.add('d-none');
});

async function assignTo(userId) {
    if (!activeConversationId) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const resp = await fetch('/dashboard/chats/assign', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ conversationId: activeConversationId, userId: userId || null })
        });

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update assignment.', 'error');
            return;
        }

        const label = userId
            ? document.querySelector(`#assignMenu [data-user-id="${userId}"]`)?.textContent.trim() || 'Assigned'
            : 'Unassigned';
        document.getElementById('assignLabel').textContent = label;
        document.getElementById('assignMenu')?.classList.add('d-none');
        loadContactDetails(activeConversationId);
        showToast(userId ? `Assigned to ${label}` : 'Conversation unassigned', 'success');
    } catch (e) {
        showToast('Network error: ' + e.message, 'error');
    }
}

async function setStatus(status) {
    if (!activeConversationId) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const resp = await fetch('/dashboard/chats/status', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ conversationId: activeConversationId, status })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update status.', 'error');
            return;
        }
        applyStatusToHeader(status);
        document.getElementById('statusMenu')?.classList.add('d-none');
        showToast(status === 0 ? 'Conversation reopened' : 'Conversation closed', 'success');
    } catch (e) {
        showToast('Network error: ' + e.message, 'error');
    }
}

function applyStatusToHeader(status) {
    const labels = ['Open', 'Snoozed', 'Closed'];
    const classes = ['', 'snoozed', 'closed'];
    const pill = document.getElementById('statusPill');
    if (!pill) return;
    pill.textContent = labels[status] ?? 'Open';
    pill.className = 'status-pill-mini' + (classes[status] ? ' ' + classes[status] : '');
}

/* ─── Contact panel ─────────────────────────────────────────────────── */
function toggleContactPanel() {
    const p = document.getElementById('contactPanel');
    if (!p) return;
    if (window.innerWidth <= 1100) {
        p.classList.toggle('show');
    } else {
        p.classList.toggle('d-none');
    }
}

function setContactTab(tab) {
    document.querySelectorAll('.cp-tab').forEach(t => t.classList.toggle('active', t.dataset.tab === tab));
    document.querySelectorAll('.cp-pane').forEach(p => p.classList.toggle('d-none', p.dataset.pane !== tab));
}

async function loadContactDetails(conversationId) {
    try {
        const resp = await fetch(`/dashboard/chats/${conversationId}/contact`);
        if (!resp.ok) return;
        const d = await resp.json();
        contactDetails = d;

        const initial = (d.displayName || d.phoneNumber || '?').charAt(0).toUpperCase();
        document.getElementById('cpAvatarLetter').textContent = initial;
        document.getElementById('cpName').textContent = d.displayName || d.phoneNumber;
        document.getElementById('cpPhone').textContent = d.phoneNumber ? '+' + d.phoneNumber : '—';
        document.getElementById('cpChannel').textContent = d.instanceDisplayName || '—';
        document.getElementById('cpAssigned').textContent = d.assignedUserName || 'Unassigned';
        document.getElementById('cpStatus').textContent = ['Open','Snoozed','Closed'][d.conversationStatus] || 'Open';
        document.getElementById('cpCreatedAt').textContent = new Date(d.contactCreatedAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
        document.getElementById('cpMsgCount').textContent = d.messageCount;
        document.getElementById('cpNotesCount').textContent = d.noteCount;

        // Sync header dropdowns
        document.getElementById('assignLabel').textContent = d.assignedUserName || 'Unassigned';
        applyStatusToHeader(d.conversationStatus);
        applyLifecycleToHeader(d.lifecycleStage ?? 0);
    } catch (e) {
        console.error('Contact details load failed:', e);
    }
}

/* ─── Load messages ─────────────────────────────────────────────────── */
async function loadMessages(conversationId) {
    const feed = document.getElementById('chatMessages');
    feed.innerHTML = '<div class="messages-loading"><div class="spinner"></div></div>';

    try {
        const resp = await fetch(`/dashboard/chats/${conversationId}/messages`);
        const messages = await resp.json();

        feed.innerHTML = '';

        if (messages.length === 0) {
            feed.innerHTML = '<div style="text-align:center;color:#bbb;padding:40px 0;font-size:13px;">No messages yet</div>';
            return;
        }

        let lastDate = null;
        messages.forEach(msg => {
            const msgDate = new Date(msg.sentAt).toDateString();
            if (msgDate !== lastDate) {
                feed.appendChild(makeDateSeparator(msg.sentAt));
                lastDate = msgDate;
            }
            feed.appendChild(buildBubble(msg));
        });

        scrollToBottom(false);
    } catch (err) {
        feed.innerHTML = '<div style="text-align:center;color:#e74c3c;padding:40px 0;">Failed to load messages.</div>';
        console.error(err);
    }
}

/* ─── Send message / Add note ──────────────────────────────────────── */
async function sendMessage(event) {
    event.preventDefault();
    if (!activeConversationId) return;

    const input = document.getElementById('messageInput');
    const btn = document.getElementById('sendBtn');
    const body = input.value.trim();
    if (!body) return;

    btn.disabled = true;
    input.disabled = true;

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const url   = composeMode === 'note' ? '/dashboard/chats/note' : '/dashboard/chats/send';

    try {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ conversationId: activeConversationId, body })
        });

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || (composeMode === 'note' ? 'Failed to add note.' : 'Failed to send message.'), 'error');
            return;
        }

        input.value = '';
        if (composeMode === 'note') {
            loadContactDetails(activeConversationId);
            showToast('Note added', 'success');
        }
    } catch (err) {
        showToast('Network error: ' + err.message, 'error');
    } finally {
        btn.disabled = false;
        input.disabled = false;
        input.focus();
    }
}

/* ─── DOM helpers ───────────────────────────────────────────────────── */
function buildBubble(msg) {
    const direction = msg.direction;
    const isOutgoing = direction === 1;
    const isNote = direction === 2;

    const wrapper = document.createElement('div');
    wrapper.className = `msg ${isNote ? 'note' : (isOutgoing ? 'outgoing' : 'incoming')}`;
    wrapper.dataset.msgId = msg.id;

    if (isNote && msg.authorName) {
        const author = document.createElement('div');
        author.className = 'msg-author';
        author.innerHTML = '🗒 ' + escapeHtml(msg.authorName);
        wrapper.appendChild(author);
    }

    const bubble = document.createElement('div');
    bubble.className = 'msg-bubble';
    bubble.textContent = msg.body;

    const meta = document.createElement('div');
    meta.className = 'msg-meta';
    meta.textContent = formatMsgTime(msg.sentAt);

    if (isOutgoing) {
        const tick = document.createElement('span');
        tick.className = 'msg-tick' + (msg.status >= 2 ? ' msg-tick-read' : '');
        tick.innerHTML = renderIcon(msg.status >= 2 ? 'check-double' : 'check', 14);
        meta.appendChild(tick);
    }

    wrapper.appendChild(bubble);
    wrapper.appendChild(meta);
    return wrapper;
}

function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function appendMessage(msg) {
    const feed = document.getElementById('chatMessages');
    // Remove "no messages" placeholder if present
    const placeholder = feed.querySelector('div[style]');
    if (placeholder) placeholder.remove();

    feed.appendChild(buildBubble(msg));
}

function makeDateSeparator(isoDate) {
    const sep = document.createElement('div');
    sep.className = 'date-sep';
    const d = new Date(isoDate);
    const today = new Date();
    const yesterday = new Date(today); yesterday.setDate(yesterday.getDate() - 1);

    let label;
    if (d.toDateString() === today.toDateString()) label = 'Today';
    else if (d.toDateString() === yesterday.toDateString()) label = 'Yesterday';
    else label = d.toLocaleDateString(undefined, { day: 'numeric', month: 'long', year: 'numeric' });

    sep.textContent = label;
    return sep;
}

function updateBadge(conversationId, count) {
    const badge = document.getElementById(`badge-${conversationId}`);
    if (!badge) return;
    if (count > 0) {
        badge.textContent = count;
        badge.classList.remove('d-none');
    } else {
        badge.classList.add('d-none');
    }
}

/**
 * Updates the unread suffix in the instance dropdown row for the given instance.
 * The dropdown rows have data-instance-id and an inner <span data-unread-suffix>.
 */
function updateInstanceDropdownUnread(instanceId, unreadTotal) {
    const row = document.querySelector(`.instance-menu-item[data-instance-id="${instanceId}"]`);
    if (!row) return;
    const suffix = row.querySelector('[data-unread-suffix]');
    if (!suffix) return;
    suffix.textContent = unreadTotal > 0 ? `, ${unreadTotal} unread` : '';
}

function updateSidebarRow(conversationId, message, unreadCount) {
    const item = document.querySelector(`.conv-item[data-id="${conversationId}"]`);
    if (!item) return;

    const preview = item.querySelector('.conv-preview');
    if (preview) {
        const text = message.body ?? '';
        preview.textContent = text.length > 55 ? text.slice(0, 55) + '…' : text;
    }

    const timeEl = item.querySelector('.conv-time');
    if (timeEl) timeEl.textContent = formatSidebarTime(message.sentAt);

    if (conversationId !== activeConversationId) {
        updateBadge(conversationId, unreadCount);
    }

    // Move row to top of list
    const list = document.getElementById('conversationList');
    list.prepend(item);
}

/* ─── Scroll helpers ────────────────────────────────────────────────── */
function scrollToBottom(smooth = true) {
    const feed = document.getElementById('chatMessages');
    feed.scrollTo({ top: feed.scrollHeight, behavior: smooth ? 'smooth' : 'instant' });
}

document.addEventListener('DOMContentLoaded', () => {
    const feed = document.getElementById('chatMessages');
    if (feed) {
        feed.addEventListener('scroll', () => {
            atBottom = feed.scrollTop + feed.clientHeight >= feed.scrollHeight - 40;
        });
    }

    // Search filter
    document.getElementById('searchInput')?.addEventListener('input', e => {
        const q = e.target.value.toLowerCase();
        document.querySelectorAll('.conv-item').forEach(item => {
            const name = item.querySelector('.conv-name')?.textContent.toLowerCase() ?? '';
            const phone = item.dataset.phone?.toLowerCase() ?? '';
            item.style.display = name.includes(q) || phone.includes(q) ? '' : 'none';
        });
    });

    // Enter to send
    document.getElementById('messageInput')?.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            document.getElementById('chatForm')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        }
    });
});

/* ─── Time formatters ───────────────────────────────────────────────── */
function formatMsgTime(iso) {
    return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatSidebarTime(iso) {
    const d = new Date(iso);
    const now = new Date();
    const diff = (now - d) / 1000;
    if (diff < 60) return 'now';
    if (diff < 3600) return `${Math.floor(diff / 60)}m`;
    if (d.toDateString() === now.toDateString()) return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
    const days = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
    if (diff < 7 * 86400) return days[d.getDay()];
    return d.toLocaleDateString(undefined, { day: '2-digit', month: '2-digit' });
}

/* ─── Client-side icon renderer ──────────────────────────────────── */
/* Mirrors the server-side _Icon partial. Same Lucide paths, identical
   styling — used only when we need to inject SVG via JavaScript
   (e.g. read receipts on dynamically-rendered message bubbles). */
const ICON_PATHS = {
    'check':         '<polyline points="20 6 9 17 4 12"/>',
    'check-double':  '<polyline points="7 11 11 15 17 9"/><polyline points="13 11 17 15 23 9"/>',
};

function renderIcon(name, size = 16) {
    const body = ICON_PATHS[name];
    if (!body) return '';
    return `<svg class="icon" width="${size}" height="${size}" viewBox="0 0 24 24" `
         + `fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" `
         + `stroke-linejoin="round" aria-hidden="true">${body}</svg>`;
}

/* ─── Toast notifications ──────────────────────────────────────────── */
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
    }, 2600);
}

/* ─── Lifecycle stage ─────────────────────────────────────────────── */
const LIFECYCLE_LABELS = [
    'New Client', 'Not Responding', 'Interested', 'Thinking',
    'Wants a Meeting', 'Waiting for Meeting', 'Discussed',
    'Potential Client', 'Will Make Payment', 'Waiting for Contract', 'Our Client'
];

const LIFECYCLE_COLORS = [
    '#94a3b8', '#ef4444', '#3b82f6', '#8b5cf6',
    '#06b6d4', '#0ea5e9', '#6366f1',
    '#f59e0b', '#eab308', '#84cc16', '#16a34a'
];

function toggleLifecycle() {
    document.getElementById('lifecycleMenu')?.classList.toggle('d-none');
    document.getElementById('assignMenu')?.classList.add('d-none');
    document.getElementById('statusMenu')?.classList.add('d-none');
}

document.addEventListener('click', (e) => {
    if (!e.target.closest('#lifecycleWrap')) {
        document.getElementById('lifecycleMenu')?.classList.add('d-none');
    }
});

function applyLifecycleToHeader(stage) {
    const btn   = document.getElementById('lifecycleBtn');
    const label = document.getElementById('lifecycleLabel');
    if (!btn || !label) return;

    btn.className = `lifecycle-chip lifecycle-stage-${stage}`;
    label.textContent = LIFECYCLE_LABELS[stage] ?? 'Unknown';

    // Mirror into the right contact panel.
    const cpLabel = document.getElementById('cpLifecycleLabel');
    const cpDot   = document.querySelector('#cpLifecycle .lc-dot');
    if (cpLabel) cpLabel.textContent = LIFECYCLE_LABELS[stage] ?? 'Unknown';
    if (cpDot)   cpDot.style.background = LIFECYCLE_COLORS[stage] ?? '#94a3b8';
}

async function setLifecycle(stage) {
    if (!activeConversationId) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    try {
        const resp = await fetch('/dashboard/chats/lifecycle', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ conversationId: activeConversationId, stage })
        });

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || 'Failed to update lifecycle stage.', 'error');
            return;
        }

        applyLifecycleToHeader(stage);
        document.getElementById('lifecycleMenu')?.classList.add('d-none');
        showToast(`Lifecycle: ${LIFECYCLE_LABELS[stage]}`, 'success');
    } catch (e) {
        showToast('Network error: ' + e.message, 'error');
    }
}
