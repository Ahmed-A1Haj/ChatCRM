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
    const { conversationId, instanceId, message, unreadCount } = data;

    // Messages for other instances — ignore (server groups should already filter, this is a belt-and-suspenders check)
    if (instanceId && activeInstanceId && instanceId !== activeInstanceId) return;

    const exists = document.querySelector(`.conv-item[data-id="${conversationId}"]`);
    if (!exists) {
        // Brand new conversation within this instance — refresh so the server-rendered sidebar picks it up
        location.reload();
        return;
    }

    updateSidebarRow(conversationId, message, unreadCount);

    if (conversationId === activeConversationId) {
        appendMessage(message);
        if (atBottom) scrollToBottom();
        fetch(`/dashboard/chats/${conversationId}/messages`);
        updateBadge(conversationId, 0);
    }
});

// Reload on disconnect event so status/UI stays honest.
connection.on('InstanceStatusChanged', ({ id, status }) => {
    if (id === activeInstanceId) {
        // Gentle refresh of sidebar header; full reload keeps dropdown in sync.
        location.reload();
    }
});

connection.start().then(async () => {
    if (activeInstanceId > 0) {
        try { await connection.invoke('JoinInstance', activeInstanceId); }
        catch (err) { console.error('JoinInstance failed:', err); }
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
function selectConversation(id) {
    if (activeConversationId === id) return;

    // Update sidebar highlight
    document.querySelectorAll('.conv-item').forEach(el => el.classList.remove('active'));
    const item = document.querySelector(`.conv-item[data-id="${id}"]`);
    if (item) item.classList.add('active');

    activeConversationId = id;

    // Show chat window, hide empty state
    document.getElementById('chatEmpty').classList.add('d-none');
    const win = document.getElementById('chatWindow');
    win.classList.remove('d-none');

    // Update header
    const phone = item?.dataset.phone ?? '';
    const name = item?.querySelector('.conv-name')?.textContent.trim() ?? phone;
    const avatarText = name.charAt(0).toUpperCase();

    document.getElementById('chatHeaderName').textContent = name;
    document.getElementById('chatHeaderPhone').textContent = phone;
    document.getElementById('chatHeaderAvatar').textContent = avatarText;

    // Load messages
    loadMessages(id);

    // Clear unread on sidebar
    updateBadge(id, 0);

    // Mobile: hide sidebar
    if (window.innerWidth <= 768) {
        document.getElementById('chatSidebar').classList.add('hidden');
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

/* ─── Send message ──────────────────────────────────────────────────── */
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

    try {
        const resp = await fetch('/dashboard/chats/send', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({ conversationId: activeConversationId, body })
        });

        if (!resp.ok) {
            const err = await resp.json();
            console.error('Send failed:', err);
            return;
        }

        input.value = '';
    } catch (err) {
        console.error('Send error:', err);
    } finally {
        btn.disabled = false;
        input.disabled = false;
        input.focus();
    }
}

/* ─── DOM helpers ───────────────────────────────────────────────────── */
function buildBubble(msg) {
    const isOutgoing = msg.direction === 1;
    const wrapper = document.createElement('div');
    wrapper.className = `msg ${isOutgoing ? 'outgoing' : 'incoming'}`;
    wrapper.dataset.msgId = msg.id;

    const bubble = document.createElement('div');
    bubble.className = 'msg-bubble';
    bubble.textContent = msg.body;

    const meta = document.createElement('div');
    meta.className = 'msg-meta';
    meta.textContent = formatMsgTime(msg.sentAt);

    if (isOutgoing) {
        const tick = document.createElement('span');
        tick.textContent = msg.status >= 2 ? ' ✓✓' : ' ✓';
        tick.style.color = msg.status >= 2 ? '#34b7f1' : '#aaa';
        meta.appendChild(tick);
    }

    wrapper.appendChild(bubble);
    wrapper.appendChild(meta);
    return wrapper;
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
