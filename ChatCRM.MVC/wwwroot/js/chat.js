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

connection.on('MessageEdited', ({ conversationId, messageId, body, editedAt }) => {
    if (conversationId === activeConversationId) applyMessageEdit(messageId, body, editedAt);
});

connection.on('MessageDeleted', ({ conversationId, messageId }) => {
    if (conversationId === activeConversationId) applyMessageDelete(messageId);
});

connection.on('MessageTranscribed', ({ conversationId, messageId, status, text, language, provider }) => {
    if (conversationId === activeConversationId) {
        applyTranscription(messageId, { status, text, language, provider });
    }
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

    // Auto-open the contact panel on wide screens when a conversation opens.
    // Mobile (≤1100px) keeps the old behavior so the panel doesn't cover the chat.
    if (window.innerWidth > 1100) {
        document.getElementById('contactPanel')?.classList.remove('d-none');
    }

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

    // Close the contact details panel when clicking outside it. Ignore clicks on the panel
    // itself, its open-triggers, the agent-picker popover it anchors (rendered outside it),
    // and the conversation list — selecting a conversation auto-opens the panel, so a sidebar
    // click must not immediately close it.
    const panel = document.getElementById('contactPanel');
    if (panel &&
        !e.target.closest('#contactPanel') &&
        !e.target.closest('#contactToggle') &&
        !e.target.closest('#chatHeaderTarget') &&
        !e.target.closest('#agentPickerPopover') &&
        !e.target.closest('#chatSidebar')) {
        const mobile = window.innerWidth <= 1100;
        const isOpen = mobile ? panel.classList.contains('show') : !panel.classList.contains('d-none');
        if (isOpen) {
            // Mirror toggleContactPanel(): 'show' drives the mobile overlay, 'd-none' the desktop column.
            panel.classList.remove('show');
            panel.classList.add('d-none');
        }
    }
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
            showToast(err.error || t('Toast.AssignFailed'), 'error');
            return;
        }

        const label = userId
            ? document.querySelector(`#assignMenu [data-user-id="${userId}"]`)?.textContent.trim() || 'Assigned'
            : 'Unassigned';
        document.getElementById('assignLabel').textContent = label;
        document.getElementById('assignMenu')?.classList.add('d-none');
        loadContactDetails(activeConversationId);
        showToast(userId ? t('Toast.AssignedTo', label) : t('Toast.Unassigned'), 'success');
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
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
        showToast(status === 0 ? t('Toast.ConversationReopened') : t('Toast.ConversationClosed'), 'success');
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
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
// Navigate to the Shared Media & Files gallery for the open conversation.
function openMediaGallery() {
    if (!activeConversationId) {
        showToast(t('Toast.SelectConversation'), 'error');
        return;
    }
    window.location.href = `/dashboard/chats/${activeConversationId}/media`;
}

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
        applyServiceWindow(d);    // 24h window pill + composer state (phase 5)
        renderAssignedAgent(d);   // AI agent assignment + popover (phase 13)
        loadConversationFiles(d.contactId);  // internal private files
    } catch (e) {
        console.error('Contact details load failed:', e);
    }
}

/* ─── Internal (private) contact files ──────────────────────────────────
   Team-only files stored in private server storage, reachable only via the
   authorized /api/contacts/{id}/files endpoints. The contact never sees them. */
const _canEditContacts = () => window.__canEditContacts__ === true || window.__canEditContacts__ === 'true';
let _cpFilesContactId = null;

function fmtFileSize(bytes) {
    if (bytes == null) return '';
    if (bytes < 1024) return bytes + ' B';
    const units = ['KB', 'MB', 'GB'];
    let v = bytes / 1024, i = 0;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return (v < 10 ? v.toFixed(1) : Math.round(v)) + ' ' + units[i];
}

function fmtFileDate(iso) {
    if (!iso) return '';
    try { return new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }); }
    catch (e) { return ''; }
}

async function loadConversationFiles(contactId) {
    _cpFilesContactId = contactId || null;
    const card = document.getElementById('cpFilesCard');
    if (!card) return;

    const upload = document.getElementById('cpFilesUpload');
    if (upload) upload.classList.toggle('d-none', !_canEditContacts());

    const list = document.getElementById('cpFilesList');
    const empty = document.getElementById('cpFilesEmpty');
    if (!_cpFilesContactId) { card.classList.add('d-none'); return; }
    card.classList.remove('d-none');
    list.innerHTML = `<div class="cp-files-loading">${escapeHtml(t('Action.Loading'))}</div>`;
    empty.classList.add('d-none');

    try {
        const res = await fetch(`/api/contacts/${_cpFilesContactId}/files`);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        renderConversationFiles(await res.json());
    } catch (e) {
        list.innerHTML = `<div class="cp-files-loading">${escapeHtml(t('ContactFiles.LoadError'))}</div>`;
    }
}

function renderConversationFiles(files) {
    const list = document.getElementById('cpFilesList');
    const empty = document.getElementById('cpFilesEmpty');
    if (!list) return;

    if (!files || files.length === 0) {
        list.innerHTML = '';
        empty.classList.remove('d-none');
        return;
    }
    empty.classList.add('d-none');

    const canEdit = _canEditContacts();
    const cid = _cpFilesContactId;
    list.innerHTML = files.map(f => {
        const dl = `/api/contacts/${cid}/files/${f.id}/download`;
        const ext = (f.fileName || 'file').split('.').pop().toUpperCase().slice(0, 4);
        const sub = [escapeHtml(fmtFileSize(f.sizeBytes)), escapeHtml(fmtFileDate(f.uploadedAtUtc))].filter(Boolean).join(' · ');
        const editBtns = canEdit ? `
            <button type="button" class="cp-file-btn" title="${escapeHtml(t('ContactFiles.Rename'))}" data-file-rename="${f.id}" data-file-name="${escapeHtml(f.fileName)}">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            </button>
            <button type="button" class="cp-file-btn cp-file-btn-danger" title="${escapeHtml(t('ContactFiles.Delete'))}" data-file-delete="${f.id}" data-file-name="${escapeHtml(f.fileName)}">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/></svg>
            </button>` : '';
        return `<div class="cp-file-row">
            <span class="cp-file-ext" aria-hidden="true">${escapeHtml(ext)}</span>
            <div class="cp-file-main">
                <a class="cp-file-name" href="${dl}?inline=1" target="_blank" rel="noopener" title="${escapeHtml(t('ContactFiles.View'))}">${escapeHtml(f.fileName)}</a>
                <span class="cp-file-sub">${sub}</span>
            </div>
            <div class="cp-file-actions">
                <a class="cp-file-btn" href="${dl}" title="${escapeHtml(t('ContactFiles.Download'))}" download>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                </a>
                ${editBtns}
            </div>
        </div>`;
    }).join('');
}

async function uploadConversationFiles(fileList) {
    if (!_cpFilesContactId || !fileList || fileList.length === 0) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const fd = new FormData();
    for (const f of fileList) fd.append('files', f);
    try {
        const res = await fetch(`/api/contacts/${_cpFilesContactId}/files`, {
            method: 'POST', headers: { 'RequestVerificationToken': token }, body: fd
        });
        if (res.status === 403) { showToast(t('ContactFiles.Forbidden'), 'error'); return; }
        if (!res.ok) { showToast(t('ContactFiles.UploadError'), 'error'); return; }
        const data = await res.json();
        (data.errors || []).forEach(er => showToast(`${er.file}: ${er.error}`, 'error'));
        if (data.uploaded && data.uploaded.length) showToast(t('ContactFiles.Uploaded', data.uploaded.length), 'success');
        loadConversationFiles(_cpFilesContactId);
    } catch (e) {
        showToast(t('ContactFiles.UploadError'), 'error');
    }
}

async function renameConversationFile(fileId, currentName) {
    const next = window.prompt(t('ContactFiles.RenamePrompt'), currentName || '');
    if (next == null) return;
    const name = next.trim();
    if (!name || name === currentName) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const res = await fetch(`/api/contacts/${_cpFilesContactId}/files/${fileId}/rename`, {
            method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ name })
        });
        if (!res.ok) { showToast(t('ContactFiles.RenameError'), 'error'); return; }
        loadConversationFiles(_cpFilesContactId);
    } catch (e) { showToast(t('ContactFiles.RenameError'), 'error'); }
}

async function deleteConversationFile(fileId, name) {
    if (!window.confirm(t('ContactFiles.DeleteConfirm', name || ''))) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const res = await fetch(`/api/contacts/${_cpFilesContactId}/files/${fileId}`, {
            method: 'DELETE', headers: { 'RequestVerificationToken': token }
        });
        if (!res.ok) { showToast(t('ContactFiles.DeleteError'), 'error'); return; }
        showToast(t('ContactFiles.Deleted'), 'success');
        loadConversationFiles(_cpFilesContactId);
    } catch (e) { showToast(t('ContactFiles.DeleteError'), 'error'); }
}

// Wire the upload button + delegated row actions once (panel markup is always present).
document.addEventListener('DOMContentLoaded', () => {
    const btn = document.getElementById('cpFilesUploadBtn');
    const input = document.getElementById('cpFilesInput');
    if (btn && input) {
        btn.addEventListener('click', () => input.click());
        input.addEventListener('change', () => { uploadConversationFiles(input.files); input.value = ''; });
    }
    const list = document.getElementById('cpFilesList');
    if (list) {
        list.addEventListener('click', (e) => {
            const ren = e.target.closest('[data-file-rename]');
            if (ren) { renameConversationFile(parseInt(ren.dataset.fileRename, 10), ren.dataset.fileName); return; }
            const del = e.target.closest('[data-file-delete]');
            if (del) { deleteConversationFile(parseInt(del.dataset.fileDelete, 10), del.dataset.fileName); return; }
        });
    }
});

/* ─── 24h customer-service window (phase 5) ─────────────────────────────
   The pill in the chat header says "Service window active — Xh left" while we're inside
   the 24-hour window after the most recent incoming message, or "Service window expired —
   template required" once it lapses. Updates every 60 seconds via the ticker so the UI
   transitions automatically without a refresh.

   Only Cloud-API (Business) instances care about the window — Personal/Baileys instances
   bypass Meta's window rules entirely, so we hide the indicator for those.

   d.integration: 0 = Personal, 1 = Business (matches WhatsAppIntegration enum). */
const INTEGRATION_BUSINESS = 1;
const SERVICE_WINDOW_HOURS = 24;
let serviceWindowTickerHandle = null;

function applyServiceWindow(details) {
    const pill = document.getElementById('serviceWindowPill');
    const banner = document.getElementById('serviceWindowExpiredBar');
    const composer = document.getElementById('chatForm');
    if (!pill || !banner) return;

    // Cancel any previous ticker; new conversation = new clock.
    if (serviceWindowTickerHandle) { clearInterval(serviceWindowTickerHandle); serviceWindowTickerHandle = null; }

    if (details.integration !== INTEGRATION_BUSINESS) {
        // Personal instance — Meta's window rules don't apply. Hide pill + banner.
        pill.classList.add('d-none');
        banner.classList.add('d-none');
        composer?.classList.remove('compose-locked');
        return;
    }

    const renderOnce = () => {
        const state = computeServiceWindowState(details.lastIncomingAtUtc);
        renderServiceWindowPill(pill, state);
        renderServiceWindowBanner(banner, composer, state);
    };

    renderOnce();
    // 60-second tick is plenty — humans don't notice sub-minute label drift.
    serviceWindowTickerHandle = setInterval(renderOnce, 60_000);
}

function computeServiceWindowState(lastIncomingAtUtc) {
    if (!lastIncomingAtUtc) return { kind: 'never' };

    const last = new Date(lastIncomingAtUtc).getTime();
    const ageMs = Date.now() - last;
    const windowMs = SERVICE_WINDOW_HOURS * 3_600_000;

    if (ageMs >= windowMs) return { kind: 'expired' };

    const remainingMin = Math.max(1, Math.floor((windowMs - ageMs) / 60_000));
    return { kind: 'active', remainingMin };
}

function renderServiceWindowPill(pill, state) {
    pill.classList.remove('is-active', 'is-expired', 'is-never');
    pill.classList.remove('d-none');
    const label = pill.querySelector('[data-service-window-label]');
    const T = window.t || ((k) => k);

    if (state.kind === 'never') {
        pill.classList.add('is-never');
        if (label) label.textContent = T('Window.Never');
        pill.title = T('Window.Never.Tooltip');
        return;
    }
    if (state.kind === 'expired') {
        pill.classList.add('is-expired');
        if (label) label.textContent = T('Window.Expired.Pill');
        pill.title = T('Window.Expired.Tooltip');
        return;
    }
    pill.classList.add('is-active');
    if (label) {
        const remaining = formatRemaining(state.remainingMin);
        label.textContent = T('Window.Active.PillFmt').replace('{0}', remaining);
    }
    pill.title = T('Window.Active.Tooltip');
}

function renderServiceWindowBanner(banner, composer, state) {
    const messageInput = document.getElementById('messageInput');
    const sendBtn = document.getElementById('sendBtn');

    if (state.kind === 'expired' || state.kind === 'never') {
        banner.classList.remove('d-none');
        composer?.classList.add('compose-locked');
        if (messageInput) {
            messageInput.disabled = true;
            messageInput.placeholder = (window.t || ((k)=>k))('Window.Expired.ComposerPlaceholder');
        }
        if (sendBtn) sendBtn.disabled = true;
        return;
    }
    // Active window — full free-form reply allowed.
    banner.classList.add('d-none');
    composer?.classList.remove('compose-locked');
    if (messageInput) {
        messageInput.disabled = false;
        messageInput.placeholder = (window.t || ((k)=>k))('Compose.TypeMessage');
    }
    if (sendBtn) sendBtn.disabled = false;
}

function formatRemaining(minutes) {
    if (minutes >= 60) {
        const h = Math.floor(minutes / 60);
        const m = minutes % 60;
        return m === 0 ? `${h}h` : `${h}h ${m}m`;
    }
    return `${minutes}m`;
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
        // Keep any still-processing transcriptions updating without a manual refresh.
        ensureTranscriptPolling();
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
            showToast(t('Toast.NoteAdded'), 'success');
        }
    } catch (err) {
        showToast(t('Toast.NetworkError', err.message), 'error');
    } finally {
        btn.disabled = false;
        input.disabled = false;
        input.focus();
    }
}

/* ─── DOM helpers ───────────────────────────────────────────────────── */
/* Message kinds — must mirror ChatCRM.Domain.Entities.MessageKind */
const MSG_KIND = { TEXT: 0, IMAGE: 1, VIDEO: 2, AUDIO: 3, DOCUMENT: 4, STICKER: 5 };

/* Transcription status — mirrors ChatCRM.Domain.Entities.TranscriptionStatus */
const TRANSCRIPT = { NONE: 0, PENDING: 1, PROCESSING: 2, DONE: 3, FAILED: 4 };

// Read-only transcription panel rendered under an audio player. Returns inner HTML for the
// .msg-transcript-wrap container, keyed off the message's transcription fields.
function transcriptionHtml(msg) {
    const status = msg.transcriptionStatus ?? TRANSCRIPT.NONE;
    if (status === TRANSCRIPT.NONE) return '';

    if (status === TRANSCRIPT.PENDING || status === TRANSCRIPT.PROCESSING) {
        return `<div class="msg-transcript msg-transcript-loading">`
            + `<span class="msg-transcript-spinner" aria-hidden="true"></span>`
            + `<span>${escapeHtml(t('Transcription.InProgress'))}</span></div>`;
    }
    if (status === TRANSCRIPT.FAILED) {
        const errTitle = msg.transcriptionError ? ` title="${escapeHtml(msg.transcriptionError)}"` : '';
        return `<div class="msg-transcript msg-transcript-failed"${errTitle}>`
            + `<span class="msg-transcript-failicon" aria-hidden="true">⚠</span>`
            + `<span>${escapeHtml(t('Transcription.Failed'))}</span>`
            + `<button type="button" class="msg-transcript-retry" data-transcript-retry="${msg.id}">${escapeHtml(t('Transcription.Retry'))}</button>`
            + `</div>`;
    }
    // Done
    const meta = [msg.transcriptionLanguage, msg.transcriptionProvider].filter(Boolean).map(escapeHtml).join(' · ');
    return `<div class="msg-transcript msg-transcript-done">`
        + `<div class="msg-transcript-head">`
        + `<span class="msg-transcript-label">${escapeHtml(t('Transcription.Label'))}</span>`
        + (meta ? `<span class="msg-transcript-meta">${meta}</span>` : '')
        + `</div>`
        + `<div class="msg-transcript-text">${escapeHtml(msg.transcriptionText || '')}</div>`
        + `</div>`;
}

// Live-update an already-rendered transcription panel (SignalR push or retry).
function applyTranscription(messageId, data) {
    const wrap = document.querySelector(`.msg-transcript-wrap[data-transcript-msg="${messageId}"]`);
    if (!wrap) return;
    wrap.innerHTML = transcriptionHtml({
        id: messageId,
        transcriptionStatus: data.status,
        transcriptionText: data.text,
        transcriptionLanguage: data.language,
        transcriptionProvider: data.provider,
        transcriptionError: data.error
    });
}

// Polling fallback: while any audio bubble is still "Transcribing…", poll its transcription
// endpoint until it resolves. This guarantees the text appears live even if the SignalR push
// is missed (tab backgrounded, reconnect gap, etc.). Self-stops when nothing is pending.
let _transcriptPollTimer = null;
function ensureTranscriptPolling() {
    if (_transcriptPollTimer) return;
    _transcriptPollTimer = setInterval(async () => {
        const loading = Array.from(document.querySelectorAll('.msg-transcript-wrap'))
            .filter(w => w.querySelector('.msg-transcript-loading'));
        if (loading.length === 0) { clearInterval(_transcriptPollTimer); _transcriptPollTimer = null; return; }
        for (const w of loading) {
            const id = parseInt(w.dataset.transcriptMsg, 10);
            try {
                const res = await fetch(`/dashboard/chats/messages/${id}/transcription`);
                if (!res.ok) continue;
                const d = await res.json();
                if (d.status >= TRANSCRIPT.DONE) {
                    applyTranscription(id, { status: d.status, text: d.text, language: d.language, provider: d.provider, error: d.error });
                }
            } catch (e) { /* keep polling */ }
        }
    }, 4000);
}

// Delegated retry — re-queues a failed transcription.
document.addEventListener('click', async (e) => {
    const btn = e.target.closest('[data-transcript-retry]');
    if (!btn) return;
    const messageId = parseInt(btn.dataset.transcriptRetry, 10);
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    applyTranscription(messageId, { status: TRANSCRIPT.PROCESSING });
    ensureTranscriptPolling();
    try {
        const res = await fetch(`/dashboard/chats/messages/${messageId}/transcribe`, {
            method: 'POST', headers: { 'RequestVerificationToken': token }
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
    } catch (err) {
        applyTranscription(messageId, { status: TRANSCRIPT.FAILED });
        showToast(t('Transcription.RetryError'), 'error');
    }
});

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

    const kind = msg.kind ?? MSG_KIND.TEXT;
    const mediaUrl = msg.mediaUrl;

    if (msg.isDeleted) {
        bubble.classList.add('msg-bubble-deleted');
        bubble.innerHTML = `<span class="msg-deleted-icon">🚫</span> Message deleted`;
    } else if (kind === MSG_KIND.TEXT || !mediaUrl) {
        // Plain text — or media that hasn't been fetched yet (placeholder).
        if (mediaUrl == null && kind !== MSG_KIND.TEXT) {
            bubble.classList.add('msg-bubble-pending');
            bubble.textContent = mediaPendingLabel(kind, msg.mediaFileName);
        } else {
            bubble.textContent = msg.body || '';
        }
    } else if (kind === MSG_KIND.IMAGE || kind === MSG_KIND.STICKER) {
        bubble.classList.add('msg-bubble-media');
        const img = document.createElement('img');
        img.className = 'msg-image';
        img.loading = 'lazy';
        img.src = mediaUrl;
        img.alt = msg.body || 'image';
        img.addEventListener('click', () => openLightbox(mediaUrl));
        bubble.appendChild(img);
        if (msg.body) {
            const cap = document.createElement('div');
            cap.className = 'msg-caption';
            cap.textContent = msg.body;
            bubble.appendChild(cap);
        }
    } else if (kind === MSG_KIND.VIDEO) {
        bubble.classList.add('msg-bubble-media');
        const v = document.createElement('video');
        v.className = 'msg-video';
        v.controls = true;
        v.preload = 'metadata';
        v.src = mediaUrl;
        bubble.appendChild(v);
        if (msg.body) {
            const cap = document.createElement('div');
            cap.className = 'msg-caption';
            cap.textContent = msg.body;
            bubble.appendChild(cap);
        }
    } else if (kind === MSG_KIND.AUDIO) {
        const a = document.createElement('audio');
        a.className = 'msg-audio';
        a.controls = true;
        a.preload = 'metadata';
        a.src = mediaUrl;
        bubble.appendChild(a);
        // Read-only speech-to-text panel under the player.
        const tc = document.createElement('div');
        tc.className = 'msg-transcript-wrap';
        tc.dataset.transcriptMsg = msg.id;
        tc.innerHTML = transcriptionHtml(msg);
        bubble.appendChild(tc);
    } else if (kind === MSG_KIND.DOCUMENT) {
        bubble.classList.add('msg-bubble-doc');
        const link = document.createElement('a');
        link.className = 'msg-doc';
        link.href = mediaUrl;
        link.target = '_blank';
        link.rel = 'noopener';
        link.download = msg.mediaFileName || '';
        link.innerHTML = `<span class="msg-doc-icon">📄</span>`
            + `<span class="msg-doc-meta">`
            + `<span class="msg-doc-name">${escapeHtml(msg.mediaFileName || 'Document')}</span>`
            + `<span class="msg-doc-hint">Click to download</span>`
            + `</span>`;
        bubble.appendChild(link);
        if (msg.body) {
            const cap = document.createElement('div');
            cap.className = 'msg-caption';
            cap.textContent = msg.body;
            bubble.appendChild(cap);
        }
    }

    const meta = document.createElement('div');
    meta.className = 'msg-meta';
    meta.textContent = formatMsgTime(msg.sentAt);

    if (msg.editedAt && !msg.isDeleted) {
        const editedTag = document.createElement('span');
        editedTag.className = 'msg-edited-tag';
        editedTag.textContent = 'edited';
        meta.appendChild(editedTag);
    }

    if (isOutgoing) {
        const tick = document.createElement('span');
        tick.className = 'msg-tick' + (msg.status >= 2 ? ' msg-tick-read' : '');
        tick.innerHTML = renderIcon(msg.status >= 2 ? 'check-double' : 'check', 14);
        meta.appendChild(tick);
    }

    // Edit / delete-for-everyone on outgoing bubbles. WhatsApp-style:
    //  • Right-click anywhere on the bubble → context menu at cursor (primary)
    //  • Long-press on touch devices → same menu
    //  • Hover kebab still present for discoverability + accessibility (keyboard / mouse-only users)
    if (isOutgoing && !msg.isDeleted) {
        wrapper.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            // Without stopPropagation, this event also bubbles up to the document-level
            // close-listener registered by the previous menu, which would kill the menu we
            // are about to open in the same tick.
            e.stopPropagation();
            openMessageActionMenuAt(msg, e.clientX, e.clientY);
        });

        // Long-press (mobile) — fires after 500ms of touch hold without movement.
        let lpTimer = null;
        let lpStart = null;
        wrapper.addEventListener('touchstart', (e) => {
            const t = e.touches[0];
            lpStart = { x: t.clientX, y: t.clientY };
            lpTimer = setTimeout(() => {
                if (navigator.vibrate) navigator.vibrate(15);
                openMessageActionMenuAt(msg, lpStart.x, lpStart.y);
                lpTimer = null;
            }, 500);
        }, { passive: true });
        const cancelLP = () => { if (lpTimer) { clearTimeout(lpTimer); lpTimer = null; } };
        wrapper.addEventListener('touchend',    cancelLP);
        wrapper.addEventListener('touchcancel', cancelLP);
        wrapper.addEventListener('touchmove',   (e) => {
            if (!lpStart || !lpTimer) return;
            const t = e.touches[0];
            if (Math.hypot(t.clientX - lpStart.x, t.clientY - lpStart.y) > 8) cancelLP();
        }, { passive: true });
    }

    wrapper.appendChild(bubble);
    wrapper.appendChild(meta);
    return wrapper;
}

function openMessageActionMenuAt(msg, clientX, clientY) {
    closeMessageActionMenus();
    const menu = document.createElement('div');
    menu.className = 'msg-action-menu';

    // Edit only makes sense on text bubbles + bubbles that have a caption (image/video/document with body).
    const canEdit = (msg.kind === MSG_KIND.TEXT) || (msg.body && msg.body.length > 0);
    if (canEdit) {
        const editItem = document.createElement('button');
        editItem.type = 'button';
        editItem.className = 'msg-action-item';
        editItem.innerHTML = renderIcon('edit', 14) + '<span>' + escapeHtml(t('Action.Edit')) + '</span>';
        editItem.addEventListener('click', () => { closeMessageActionMenus(); promptEdit(msg); });
        menu.appendChild(editItem);
    }

    const deleteItem = document.createElement('button');
    deleteItem.type = 'button';
    deleteItem.className = 'msg-action-item msg-action-danger';
    deleteItem.innerHTML = renderIcon('trash', 14) + '<span>' + escapeHtml(t('Action.Delete')) + '</span>';
    deleteItem.addEventListener('click', () => { closeMessageActionMenus(); confirmDelete(msg); });
    menu.appendChild(deleteItem);

    // Render off-screen first so we can measure dimensions, then clamp into the viewport.
    menu.style.position = 'fixed';
    menu.style.left = '-9999px';
    menu.style.top  = '-9999px';
    document.body.appendChild(menu);

    const pad = 8;
    const mw = menu.offsetWidth;
    const mh = menu.offsetHeight;
    const vw = window.innerWidth;
    const vh = window.innerHeight;

    let x = clientX;
    let y = clientY;
    if (x + mw + pad > vw) x = vw - mw - pad;     // would overflow right
    if (y + mh + pad > vh) y = clientY - mh;      // flip above the cursor
    if (x < pad) x = pad;
    if (y < pad) y = pad;

    menu.style.left = x + 'px';
    menu.style.top  = y + 'px';

    // Highlight the target bubble while its menu is open (WhatsApp-Web style).
    const wrapper = document.querySelector(`.msg[data-msg-id="${msg.id}"]`);
    wrapper?.classList.add('msg-menu-open');

    // Lifecycle of the dismissal listeners: register on the next tick so the click/contextmenu
    // that opened this menu doesn't immediately close it, then remove them ALL when the menu
    // goes away (whether by user pick, click-outside, Esc, or another menu opening on top).
    const dismiss = () => closeMessageActionMenus();
    const onKey   = (e) => { if (e.key === 'Escape') closeMessageActionMenus(); };

    let armed = false;
    setTimeout(() => {
        document.addEventListener('click',       dismiss);
        document.addEventListener('contextmenu', dismiss);
        document.addEventListener('keydown',     onKey);
        window.addEventListener  ('blur',        dismiss);
        armed = true;
    }, 0);

    menu._cleanup = () => {
        wrapper?.classList.remove('msg-menu-open');
        if (!armed) return;
        document.removeEventListener('click',       dismiss);
        document.removeEventListener('contextmenu', dismiss);
        document.removeEventListener('keydown',     onKey);
        window.removeEventListener  ('blur',        dismiss);
    };
}

function closeMessageActionMenus() {
    document.querySelectorAll('.msg-action-menu').forEach(m => {
        try { m._cleanup?.(); } catch { /* noop */ }
        m.remove();
    });
}

async function promptEdit(msg) {
    const trimmed = await openEditModal(msg.body || '');
    if (trimmed == null || !trimmed || trimmed === (msg.body ?? '')) return;

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const resp = await fetch('/dashboard/chats/edit', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ messageId: msg.id, body: trimmed })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('Toast.EditFailed'), 'error');
            return;
        }
        // SignalR will broadcast MessageEdited to all tabs (including this one) — UI updates from there.
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

async function confirmDelete(msg) {
    const ok = await openConfirmModal({
        title: t('Modal.Delete.Title'),
        body: t('Modal.Delete.Body'),
        confirmLabel: t('Action.Delete'),
        cancelLabel: t('Action.Cancel'),
        danger: true
    });
    if (!ok) return;

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const resp = await fetch('/dashboard/chats/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ messageId: msg.id })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('Toast.DeleteFailed'), 'error');
            return;
        }
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

/* ─── In-app modals (replace window.prompt / window.confirm) ─────── */

function openModalShell(content) {
    const overlay = document.createElement('div');
    overlay.className = 'app-modal-overlay';
    overlay.appendChild(content);
    document.body.appendChild(overlay);
    // Trap focus + clicks-outside-to-close
    overlay.addEventListener('click', (e) => { if (e.target === overlay) overlay.dataset.dismiss = 'true'; });
    return overlay;
}

function openEditModal(initialText) {
    return new Promise((resolve) => {
        const dlg = document.createElement('div');
        dlg.className = 'app-modal';
        dlg.innerHTML = `
            <div class="app-modal-head">
                <h3>${escapeHtml(t('Modal.Edit.Title'))}</h3>
                <button type="button" class="app-modal-close" aria-label="${escapeHtml(t('Action.Close'))}">×</button>
            </div>
            <div class="app-modal-body">
                <textarea class="app-modal-textarea" rows="4"></textarea>
                <p class="app-modal-hint">${escapeHtml(t('Modal.Edit.Hint'))}</p>
            </div>
            <div class="app-modal-foot">
                <button type="button" class="app-modal-btn app-modal-btn-ghost" data-action="cancel">${escapeHtml(t('Action.Cancel'))}</button>
                <button type="button" class="app-modal-btn app-modal-btn-primary" data-action="save">${escapeHtml(t('Action.Save'))}</button>
            </div>
        `;
        const overlay = openModalShell(dlg);
        const ta = dlg.querySelector('textarea');
        ta.value = initialText;
        setTimeout(() => { ta.focus(); ta.setSelectionRange(ta.value.length, ta.value.length); }, 0);

        const close = (val) => { overlay.remove(); document.removeEventListener('keydown', onKey); resolve(val); };
        const onKey = (e) => {
            if (e.key === 'Escape') close(null);
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) close(ta.value.trim());
        };
        document.addEventListener('keydown', onKey);
        dlg.querySelector('[data-action="save"]').onclick = () => close(ta.value.trim());
        dlg.querySelector('[data-action="cancel"]').onclick = () => close(null);
        dlg.querySelector('.app-modal-close').onclick = () => close(null);
        const watchOverlay = setInterval(() => {
            if (overlay.dataset.dismiss === 'true') { clearInterval(watchOverlay); close(null); }
        }, 50);
    });
}

function openConfirmModal({ title, body, confirmLabel = 'Confirm', cancelLabel = 'Cancel', danger = false }) {
    return new Promise((resolve) => {
        const dlg = document.createElement('div');
        dlg.className = 'app-modal app-modal-narrow';
        dlg.innerHTML = `
            <div class="app-modal-head">
                <h3></h3>
                <button type="button" class="app-modal-close" aria-label="Close">×</button>
            </div>
            <div class="app-modal-body">
                <p class="app-modal-text"></p>
            </div>
            <div class="app-modal-foot">
                <button type="button" class="app-modal-btn app-modal-btn-ghost" data-action="cancel"></button>
                <button type="button" class="app-modal-btn ${danger ? 'app-modal-btn-danger' : 'app-modal-btn-primary'}" data-action="ok"></button>
            </div>
        `;
        dlg.querySelector('h3').textContent = title;
        dlg.querySelector('.app-modal-text').textContent = body;
        dlg.querySelector('[data-action="cancel"]').textContent = cancelLabel;
        dlg.querySelector('[data-action="ok"]').textContent = confirmLabel;

        const overlay = openModalShell(dlg);
        const close = (val) => { overlay.remove(); document.removeEventListener('keydown', onKey); resolve(val); };
        const onKey = (e) => {
            if (e.key === 'Escape') close(false);
            if (e.key === 'Enter') close(true);
        };
        document.addEventListener('keydown', onKey);
        dlg.querySelector('[data-action="ok"]').onclick = () => close(true);
        dlg.querySelector('[data-action="cancel"]').onclick = () => close(false);
        dlg.querySelector('.app-modal-close').onclick = () => close(false);
        const watch = setInterval(() => {
            if (overlay.dataset.dismiss === 'true') { clearInterval(watch); close(false); }
        }, 50);
    });
}

/// Caption modal used by the file picker AND the paste handler.
/// Shows the file/preview, lets the user write a caption, returns either the caption (string,
/// possibly empty) or null when the user cancels.
function openCaptionModal(file) {
    return new Promise((resolve) => {
        const dlg = document.createElement('div');
        dlg.className = 'app-modal';
        dlg.innerHTML = `
            <div class="app-modal-head">
                <h3>${escapeHtml(t('Modal.Caption.Title'))}</h3>
                <button type="button" class="app-modal-close" aria-label="${escapeHtml(t('Action.Close'))}">×</button>
            </div>
            <div class="app-modal-body">
                <div class="app-modal-preview"></div>
                <input type="text" class="app-modal-input" placeholder="${escapeHtml(t('Modal.Caption.Placeholder'))}" maxlength="1024" />
            </div>
            <div class="app-modal-foot">
                <button type="button" class="app-modal-btn app-modal-btn-ghost" data-action="cancel">${escapeHtml(t('Action.Cancel'))}</button>
                <button type="button" class="app-modal-btn app-modal-btn-primary" data-action="send">${escapeHtml(t('Action.Send'))}</button>
            </div>
        `;
        const preview = dlg.querySelector('.app-modal-preview');
        if (file.type.startsWith('image/')) {
            const img = document.createElement('img');
            img.src = URL.createObjectURL(file);
            img.onload = () => URL.revokeObjectURL(img.src);
            preview.appendChild(img);
        } else if (file.type.startsWith('video/')) {
            const v = document.createElement('video');
            v.src = URL.createObjectURL(file);
            v.controls = true;
            v.preload = 'metadata';
            preview.appendChild(v);
        } else {
            preview.innerHTML = `<div class="app-modal-file-card">
                <div class="app-modal-file-icon">📎</div>
                <div>
                    <div class="app-modal-file-name">${escapeHtml(file.name)}</div>
                    <div class="app-modal-file-size">${(file.size / 1024).toFixed(1)} KB · ${escapeHtml(file.type || 'unknown')}</div>
                </div>
            </div>`;
        }

        const overlay = openModalShell(dlg);
        const input = dlg.querySelector('input');
        setTimeout(() => input.focus(), 0);

        const close = (val) => { overlay.remove(); document.removeEventListener('keydown', onKey); resolve(val); };
        const onKey = (e) => {
            if (e.key === 'Escape') close(null);
            if (e.key === 'Enter') close(input.value.trim());
        };
        document.addEventListener('keydown', onKey);
        dlg.querySelector('[data-action="send"]').onclick = () => close(input.value.trim());
        dlg.querySelector('[data-action="cancel"]').onclick = () => close(null);
        dlg.querySelector('.app-modal-close').onclick = () => close(null);
        const watch = setInterval(() => {
            if (overlay.dataset.dismiss === 'true') { clearInterval(watch); close(null); }
        }, 50);
    });
}

function applyMessageEdit(messageId, body, editedAtIso) {
    const wrapper = document.querySelector(`.msg[data-msg-id="${messageId}"]`);
    if (!wrapper) return;
    const bubble = wrapper.querySelector('.msg-bubble');
    if (!bubble) return;
    // For text bubbles we replace .textContent. For media bubbles, the body is the caption —
    // find or create a .msg-caption element instead of stomping the <img>/<video>/etc.
    if (bubble.classList.contains('msg-bubble-media') || bubble.classList.contains('msg-bubble-doc')) {
        let cap = bubble.querySelector('.msg-caption');
        if (!cap) {
            cap = document.createElement('div');
            cap.className = 'msg-caption';
            bubble.appendChild(cap);
        }
        cap.textContent = body;
    } else {
        bubble.textContent = body;
    }
    const meta = wrapper.querySelector('.msg-meta');
    if (meta && !meta.querySelector('.msg-edited-tag')) {
        const tag = document.createElement('span');
        tag.className = 'msg-edited-tag';
        tag.textContent = 'edited';
        // Insert before the read-receipt tick if present
        const tick = meta.querySelector('.msg-tick');
        if (tick) meta.insertBefore(tag, tick);
        else meta.appendChild(tag);
    }
}

function applyMessageDelete(messageId) {
    const wrapper = document.querySelector(`.msg[data-msg-id="${messageId}"]`);
    if (!wrapper) return;
    const bubble = wrapper.querySelector('.msg-bubble');
    if (!bubble) return;
    bubble.className = 'msg-bubble msg-bubble-deleted';
    bubble.innerHTML = `<span class="msg-deleted-icon">🚫</span> Message deleted`;
    // Strip any "edited" tag from the meta row.
    wrapper.querySelector('.msg-edited-tag')?.remove();
}

function mediaPendingLabel(kind, fileName) {
    switch (kind) {
        case MSG_KIND.IMAGE:    return '📷 Photo (loading…)';
        case MSG_KIND.VIDEO:    return '🎥 Video (loading…)';
        case MSG_KIND.AUDIO:    return '🎤 Audio (loading…)';
        case MSG_KIND.STICKER:  return '📌 Sticker';
        case MSG_KIND.DOCUMENT: return '📎 ' + (fileName || 'Document');
        default:                return '';
    }
}

/* Sidebar preview — what to show under the contact name when the last message
   is a media item with no caption. */
function previewForMessage(msg) {
    if (msg.body) return msg.body;
    switch (msg.kind ?? MSG_KIND.TEXT) {
        case MSG_KIND.IMAGE:    return '📷 Photo';
        case MSG_KIND.VIDEO:    return '🎥 Video';
        case MSG_KIND.AUDIO:    return '🎤 Audio';
        case MSG_KIND.STICKER:  return '📌 Sticker';
        case MSG_KIND.DOCUMENT: return '📎 ' + (msg.mediaFileName || 'Document');
        default:                return '';
    }
}

function openLightbox(src) {
    const overlay = document.createElement('div');
    overlay.className = 'msg-lightbox';
    overlay.innerHTML = `<img src="${escapeHtml(src)}" alt="">`;
    overlay.addEventListener('click', () => overlay.remove());
    document.body.appendChild(overlay);
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
    // If this bubble is an audio still being transcribed, make sure the poller is running.
    ensureTranscriptPolling();
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
        const text = previewForMessage(message);
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
    'more-vertical': '<circle cx="12" cy="12" r="1"/><circle cx="12" cy="5" r="1"/><circle cx="12" cy="19" r="1"/>',
    'more-horizontal':'<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
    'edit':          '<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>',
    'trash':         '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>',
    'chevron-down':  '<polyline points="6 9 12 15 18 9"/>',
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
/* Labels are pulled lazily from the i18n dictionary so a language switch
   doesn't require a redeploy of the JS. Keys mirror Lifecycle.* in the
   JSON resource files. */
const LIFECYCLE_KEYS = [
    'Lifecycle.NewClient', 'Lifecycle.NotResponding', 'Lifecycle.Interested', 'Lifecycle.Thinking',
    'Lifecycle.WantsAMeeting', 'Lifecycle.WaitingForMeeting', 'Lifecycle.Discussed',
    'Lifecycle.PotentialClient', 'Lifecycle.WillMakePayment', 'Lifecycle.WaitingForContract', 'Lifecycle.OurClient'
];
const LIFECYCLE_LABELS = new Proxy(LIFECYCLE_KEYS, {
    get(target, prop) {
        if (typeof prop === 'string' && /^\d+$/.test(prop)) {
            const key = target[Number(prop)];
            return key ? (window.t ? window.t(key) : key.replace(/^Lifecycle\./, '')) : undefined;
        }
        return Reflect.get(target, prop);
    }
});

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
        showToast(t('Toast.LifecycleUpdated', LIFECYCLE_LABELS[stage]), 'success');
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

/* ─── Attach (image / file) ────────────────────────────────────────── */
async function sendAttachment(event) {
    const file = event.target.files?.[0];
    event.target.value = ''; // reset so the same file can be picked twice
    if (!file) return;
    const caption = await openCaptionModal(file);
    if (caption == null) return; // user cancelled
    await uploadMediaFile(file, caption);
}

/// Shared upload helper used by file picker, paste handler, and (eventually) drag-and-drop.
async function uploadMediaFile(file, caption) {
    if (!file || !activeConversationId) return;

    if (file.size > 30 * 1024 * 1024) {
        showToast(t('Toast.FileTooLarge'), 'error');
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const fd = new FormData();
    fd.append('conversationId', String(activeConversationId));
    fd.append('file', file);
    if (caption && caption.trim()) fd.append('caption', caption.trim());

    showToast(t('Toast.Uploading'), 'info');
    try {
        const resp = await fetch('/dashboard/chats/send-media', {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: fd
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('Toast.UploadFailed'), 'error');
            return;
        }
        // SignalR delivers the new bubble back to this tab.
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

/// Clipboard paste — picks up images copied from anywhere (browser, screenshot tool,
/// file explorer) and sends them through the same upload pipeline.
function setupPasteHandler() {
    const handler = async (e) => {
        if (!activeConversationId) return;
        const items = e.clipboardData?.items;
        if (!items || items.length === 0) return;

        for (const item of items) {
            if (item.kind === 'file' && (item.type.startsWith('image/') || item.type.startsWith('video/'))) {
                const file = item.getAsFile();
                if (!file) continue;
                e.preventDefault();
                // Browsers paste clipboard images as "image.png" — give it a friendlier name with a timestamp.
                const ext = (file.type.split('/')[1] || 'png').split('+')[0];
                const stamped = new File([file], `pasted-${Date.now()}.${ext}`, { type: file.type });
                const caption = await openCaptionModal(stamped);
                if (caption == null) return;
                await uploadMediaFile(stamped, caption);
                return;
            }
        }
    };

    // Listen on the message input (most natural place to paste) AND on the chat window
    // (so paste works even when the input doesn't have focus).
    document.getElementById('messageInput')?.addEventListener('paste', handler);
    document.getElementById('chatMessages')?.addEventListener('paste', handler);
    // Make chatMessages focusable so it can receive paste events at all.
    document.getElementById('chatMessages')?.setAttribute('tabindex', '0');
}

document.addEventListener('DOMContentLoaded', setupPasteHandler);

/* ─── Voice notes (click-to-toggle) ─────────────────────────────────
   Single click on the mic starts recording; second click stops and sends.
   Esc or the cancel button in the recording bar discards. */
let voiceRecorder = null;
let voiceChunks = [];
let voiceStartTs = 0;
let voiceTimer = null;
let voiceCancelled = false;

function setupVoiceButton() {
    const btn = document.getElementById('voiceBtn');
    if (!btn) return;
    btn.addEventListener('click', toggleVoice);
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && voiceRecorder) cancelVoice();
    });
    document.getElementById('voiceCancelBtn')?.addEventListener('click', cancelVoice);
}

function toggleVoice() {
    if (voiceRecorder) stopVoice();
    else startVoice();
}

async function startVoice() {
    if (voiceRecorder || !activeConversationId) {
        if (!activeConversationId) showToast(t('Toast.SelectConversation'), 'error');
        return;
    }
    voiceCancelled = false;
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        // WebM/Opus is universally supported in Chromium/Firefox; iOS Safari falls back to mp4.
        const mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus'
                  : MediaRecorder.isTypeSupported('audio/ogg;codecs=opus') ? 'audio/ogg;codecs=opus'
                  : '';
        voiceRecorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
        voiceChunks = [];
        voiceRecorder.ondataavailable = (e) => { if (e.data && e.data.size > 0) voiceChunks.push(e.data); };
        voiceRecorder.onstop = async () => {
            stream.getTracks().forEach(t => t.stop());
            document.getElementById('voiceRecordingBar')?.classList.add('d-none');
            document.getElementById('voiceBtn')?.classList.remove('is-recording');
            clearInterval(voiceTimer);
            const wasCancelled = voiceCancelled;
            const chunks = voiceChunks;
            const recMime = voiceRecorder.mimeType || 'audio/webm';
            voiceRecorder = null;
            if (wasCancelled || chunks.length === 0) return;
            await uploadVoice(new Blob(chunks, { type: recMime }));
        };
        voiceRecorder.start();
        voiceStartTs = Date.now();
        document.getElementById('voiceRecordingBar')?.classList.remove('d-none');
        document.getElementById('voiceBtn')?.classList.add('is-recording');
        document.getElementById('voiceRecTime').textContent = '0:00';
        voiceTimer = setInterval(() => {
            const s = Math.floor((Date.now() - voiceStartTs) / 1000);
            document.getElementById('voiceRecTime').textContent = `${Math.floor(s/60)}:${String(s%60).padStart(2,'0')}`;
        }, 250);
    } catch (e) {
        showToast(t('Toast.MicDenied'), 'error');
        voiceRecorder = null;
    }
}

function stopVoice() {
    if (!voiceRecorder) return;
    // Refuse to send a sub-half-second blip — likely an accidental double-click.
    if (Date.now() - voiceStartTs < 500) {
        cancelVoice();
        return;
    }
    voiceRecorder.stop();
}

function cancelVoice() {
    if (!voiceRecorder) return;
    voiceCancelled = true;
    voiceRecorder.stop();
}

async function uploadVoice(blob) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const fd = new FormData();
    fd.append('conversationId', String(activeConversationId));
    fd.append('audio', blob, 'voice.webm');

    showToast(t('Toast.SendingVoice'), 'info');
    try {
        const resp = await fetch('/dashboard/chats/send-voice', {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: fd
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('Toast.VoiceFailed'), 'error');
        }
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

document.addEventListener('DOMContentLoaded', setupVoiceButton);

// ── Template picker (phase 7) ───────────────────────────────────────────
// Opens from #pickTemplateBtn in the service-window-expired bar. Lists Approved
// templates, lets the agent fill variables, and posts to /dashboard/chats/send-template.

let _tplCache = null;
let _tplSelected = null;

async function loadApprovedTemplates() {
    if (_tplCache) return _tplCache;
    try {
        const resp = await fetch('/dashboard/templates/approved');
        if (!resp.ok) return [];
        _tplCache = await resp.json();
        return _tplCache;
    } catch {
        return [];
    }
}

function renderTemplateList(list, query) {
    const ul = document.getElementById('tplPickerList');
    const empty = document.getElementById('tplPickerEmpty');
    const q = (query || '').toLowerCase().trim();
    const filtered = q
        ? list.filter(t => t.name.toLowerCase().includes(q) || t.body.toLowerCase().includes(q))
        : list;

    ul.innerHTML = '';
    if (!filtered.length) {
        empty.classList.remove('d-none');
        return;
    }
    empty.classList.add('d-none');
    filtered.forEach(tpl => {
        const li = document.createElement('li');
        li.className = 'tpl-picker-item';
        li.dataset.id = tpl.id;
        const cat = tpl.category === 1 ? t('Templates.Category.Utility')
                  : tpl.category === 2 ? t('Templates.Category.Authentication')
                  : tpl.category === 3 ? t('Templates.Category.Marketing')
                  : '—';
        li.innerHTML = `
            <div class="tpl-picker-item-head">
                <span class="tpl-picker-item-name">${escapeHtml(tpl.name)}</span>
                <span class="tpl-picker-item-meta">${escapeHtml(cat)} · ${escapeHtml(tpl.languageCode)}</span>
            </div>
            <div class="tpl-picker-item-body">${escapeHtml(tpl.body.length > 140 ? tpl.body.slice(0, 140) + '…' : tpl.body)}</div>
        `;
        li.addEventListener('click', () => selectTemplate(tpl));
        ul.appendChild(li);
    });
}

function selectTemplate(tpl) {
    _tplSelected = tpl;
    document.querySelector('.tpl-picker-body').classList.add('d-none');
    document.getElementById('tplPickerDetail').classList.remove('d-none');
    document.getElementById('tplPickerDetailName').textContent = tpl.name + ' · ' + tpl.languageCode;
    const cat = tpl.category === 1 ? t('Templates.Category.Utility')
              : tpl.category === 2 ? t('Templates.Category.Authentication')
              : tpl.category === 3 ? t('Templates.Category.Marketing')
              : '—';
    document.getElementById('tplPickerDetailCat').textContent = cat;

    // Render variable inputs.
    const vars = document.getElementById('tplPickerVars');
    vars.innerHTML = '';
    for (let i = 1; i <= tpl.variableCount; i++) {
        const row = document.createElement('div');
        row.className = 'tpl-picker-var';
        row.innerHTML = `
            <label>{{${i}}}</label>
            <input type="text" data-var-index="${i}" maxlength="200" />
        `;
        vars.appendChild(row);
    }

    renderTemplatePreview();
    vars.querySelectorAll('input[data-var-index]').forEach(inp => {
        inp.addEventListener('input', renderTemplatePreview);
    });
    if (tpl.variableCount > 0) {
        vars.querySelector('input')?.focus();
    }
}

function renderTemplatePreview() {
    if (!_tplSelected) return;
    let body = _tplSelected.body;
    document.querySelectorAll('#tplPickerVars input[data-var-index]').forEach(inp => {
        const i = inp.dataset.varIndex;
        const value = inp.value || `{{${i}}}`;
        body = body.split(`{{${i}}}`).join(value);
    });
    let html = escapeHtml(body).replace(/\n/g, '<br>');
    if (_tplSelected.footer) {
        html += '<div class="tpl-picker-preview-footer">' + escapeHtml(_tplSelected.footer) + '</div>';
    }
    document.getElementById('tplPickerPreview').innerHTML = html;
}

function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

async function openTemplatePicker() {
    if (!activeConversationId) {
        showToast(t('Toast.SelectConversation'), 'error');
        return;
    }
    const modal = document.getElementById('tplPickerModal');
    modal.classList.remove('d-none');
    document.querySelector('.tpl-picker-body').classList.remove('d-none');
    document.getElementById('tplPickerDetail').classList.add('d-none');
    document.getElementById('tplPickerSearch').value = '';
    _tplSelected = null;

    const list = await loadApprovedTemplates();
    renderTemplateList(list, '');
    document.getElementById('tplPickerSearch').focus();
}

function closeTemplatePicker() {
    document.getElementById('tplPickerModal').classList.add('d-none');
    _tplSelected = null;
}

async function sendSelectedTemplate() {
    if (!_tplSelected || !activeConversationId) return;
    const inputs = Array.from(document.querySelectorAll('#tplPickerVars input[data-var-index]'));
    const variables = inputs.map(i => i.value.trim());
    if (variables.some(v => !v)) {
        showToast(t('TemplatePicker.MissingVars'), 'error');
        return;
    }

    const btn = document.getElementById('tplPickerSend');
    btn.disabled = true;
    try {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
        const resp = await fetch('/dashboard/chats/send-template', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({
                conversationId: activeConversationId,
                templateId: _tplSelected.id,
                variables
            })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('TemplatePicker.SendFailed'), 'error');
            return;
        }
        closeTemplatePicker();
        showToast(t('TemplatePicker.Sent'), 'success');
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    } finally {
        btn.disabled = false;
    }
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('pickTemplateBtn')?.addEventListener('click', openTemplatePicker);
    document.getElementById('tplPickerClose')?.addEventListener('click', closeTemplatePicker);
    document.getElementById('tplPickerCancel')?.addEventListener('click', closeTemplatePicker);
    document.getElementById('tplPickerBack')?.addEventListener('click', () => {
        document.querySelector('.tpl-picker-body').classList.remove('d-none');
        document.getElementById('tplPickerDetail').classList.add('d-none');
        _tplSelected = null;
    });
    document.getElementById('tplPickerSend')?.addEventListener('click', sendSelectedTemplate);
    document.getElementById('tplPickerSearch')?.addEventListener('input', e => {
        renderTemplateList(_tplCache || [], e.target.value);
    });
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && !document.getElementById('tplPickerModal')?.classList.contains('d-none')) {
            closeTemplatePicker();
        }
    });
});

// ── AI Agent assignment (phase 13) ───────────────────────────────────────
// Renders the assigned-agent button on the contact panel and opens a small
// popover that lists active agents. Selecting an agent (or "Unassign") POSTs
// to /dashboard/conversations/{id}/agent.

let _agentCache = null;

function renderAssignedAgent(d) {
    const btn = document.getElementById('cpAgentBtn');
    if (!btn) return;
    const avatar = document.getElementById('cpAgentAvatar');
    const name = document.getElementById('cpAgentName');
    if (d.assignedAgentId && d.assignedAgentName) {
        if (d.assignedAgentAvatarPath) {
            avatar.innerHTML = `<img src="${d.assignedAgentAvatarPath}" alt="" />`;
        } else {
            avatar.textContent = '🤖';
        }
        name.textContent = d.assignedAgentName;
    } else {
        avatar.textContent = '🤖';
        name.textContent = t('Agents.Card.None');
    }
}

async function openAgentPicker() {
    if (!activeConversationId) return;
    const popover = document.getElementById('agentPickerPopover');
    const btn = document.getElementById('cpAgentBtn');
    if (!popover || !btn) return;

    if (!_agentCache) {
        try {
            const resp = await fetch('/dashboard/agents/picker');
            _agentCache = resp.ok ? await resp.json() : [];
        } catch { _agentCache = []; }
    }

    renderAgentPickerList();

    // Anchor below the button.
    const r = btn.getBoundingClientRect();
    popover.style.top = (r.bottom + 6) + 'px';
    popover.style.left = (r.left) + 'px';
    popover.classList.remove('d-none');
}

function closeAgentPicker() {
    document.getElementById('agentPickerPopover')?.classList.add('d-none');
}

function renderAgentPickerList() {
    const list = document.getElementById('agentPickerList');
    const empty = document.getElementById('agentPickerEmpty');
    if (!list) return;
    list.innerHTML = '';
    const items = _agentCache || [];
    if (!items.length) {
        empty.classList.remove('d-none');
        return;
    }
    empty.classList.add('d-none');

    const currentId = contactDetails?.assignedAgentId;

    items.forEach(a => {
        const li = document.createElement('li');
        li.className = (a.id === currentId) ? 'is-selected' : '';
        li.dataset.id = a.id;
        const avatar = a.avatarPath
            ? `<img src="${a.avatarPath}" alt="" />`
            : `🤖`;
        li.innerHTML = `
            <span class="ag-picker-row-avatar">${avatar}</span>
            <span class="ag-picker-row-name">${escapePickHtml(a.name)}</span>
            ${a.isDefault ? `<span class="ag-picker-row-default">${escapePickHtml(t('Agents.Status.Default'))}</span>` : ''}
        `;
        li.addEventListener('click', () => assignAgent(a.id));
        list.appendChild(li);
    });

    // "Unassign" entry.
    const unassign = document.createElement('li');
    unassign.className = 'is-unassign' + (currentId == null ? ' is-selected' : '');
    unassign.innerHTML = `<span class="ag-picker-row-name">${escapePickHtml(t('Agents.Picker.Unassign'))}</span>`;
    unassign.addEventListener('click', () => assignAgent(null));
    list.appendChild(unassign);
}

function escapePickHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

async function assignAgent(agentId) {
    if (!activeConversationId) return;
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    try {
        const resp = await fetch(`/dashboard/conversations/${activeConversationId}/agent`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
            body: JSON.stringify({ agentId })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showToast(err.error || t('Agents.Toast.AssignFailed'), 'error');
            return;
        }
        closeAgentPicker();
        await loadContactDetails(activeConversationId);
        showToast(agentId == null ? t('Agents.Toast.Unassigned') : t('Agents.Toast.Assigned'), 'success');
    } catch (e) {
        showToast(t('Toast.NetworkError', e.message), 'error');
    }
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('cpAgentBtn')?.addEventListener('click', e => {
        e.stopPropagation();
        const popover = document.getElementById('agentPickerPopover');
        if (popover && !popover.classList.contains('d-none')) closeAgentPicker();
        else openAgentPicker();
    });
    document.addEventListener('click', e => {
        const popover = document.getElementById('agentPickerPopover');
        if (!popover || popover.classList.contains('d-none')) return;
        if (popover.contains(e.target)) return;
        if (e.target.closest('#cpAgentBtn')) return;
        closeAgentPicker();
    });
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') closeAgentPicker();
    });
});
