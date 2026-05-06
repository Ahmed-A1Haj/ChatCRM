/* i18n fallback — guarantee t() is callable even if i18n.js failed to load
   (cached HTML from an older deploy, blocked CDN, etc.). The page-level inline
   script still injects window.__i18n__ before this file runs, so translations
   are available; we just make sure the function itself exists. */
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

/* ─── State ──────────────────────────────────────────────────────── */
let creatingInstanceId = null;
let qrPollHandle = null;
let statusPollHandle = null;
let disconnectTargetId = null;

const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

/* ─── SignalR — surgical live updates per card, no full-page reloads ─── */
const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .withAutomaticReconnect()
    .build();

const STATUS_NAMES  = ['pending', 'connecting', 'connected', 'disconnected'];
const STATUS_LABEL_KEYS = ['WhatsApp.Status.Pending', 'WhatsApp.Status.Connecting', 'WhatsApp.Status.Connected', 'WhatsApp.Status.Disconnected'];

hub.on('InstanceStatusChanged', ({ id, status }) => {
    updateCardStatus(id, status);
});

hub.on('ReceiveMessage', ({ instanceId, instanceUnread, instanceChatCount }) => {
    if (instanceId == null) return;
    updateCardCounters(instanceId, instanceChatCount, instanceUnread);
});

hub.on('ConversationRead', ({ instanceId, instanceUnread }) => {
    if (instanceId == null) return;
    updateCardCounters(instanceId, null, instanceUnread);
});

hub.on('InstanceDeleted', ({ id }) => {
    document.querySelector(`.instance-card[data-id="${id}"]`)?.remove();
});

// Subscribe to every visible instance's group so we receive their counter updates.
async function subscribeToAllVisibleInstances() {
    const cards = document.querySelectorAll('.instance-card[data-id]');
    for (const card of cards) {
        const id = parseInt(card.dataset.id, 10);
        if (id) {
            try { await hub.invoke('JoinInstance', id); }
            catch (err) { console.warn('JoinInstance failed for', id, err); }
        }
    }
}

hub.start()
    .then(subscribeToAllVisibleInstances)
    .catch(err => console.error('SignalR error:', err));

hub.onreconnected(() => subscribeToAllVisibleInstances());

/* ─── DOM mutators (per-card surgical updates) ───────────────────── */
function updateCardCounters(instanceId, chatCount, unread) {
    const card = document.querySelector(`.instance-card[data-id="${instanceId}"]`);
    if (!card) return;

    if (chatCount != null) {
        const el = card.querySelector('[data-conv-count]');
        if (el) el.textContent = chatCount;
    }
    if (unread != null) {
        const el = card.querySelector('[data-unread-count]');
        if (el) el.textContent = unread;
    }
}

function updateCardStatus(instanceId, status) {
    const card = document.querySelector(`.instance-card[data-id="${instanceId}"]`);
    if (!card) return;

    const name  = STATUS_NAMES[status]  ?? 'disconnected';
    const labelKey = STATUS_LABEL_KEYS[status];
    const label = labelKey ? t(labelKey) : '—';

    card.dataset.cardStatus = name;          // toggles which actions are visible (CSS-driven)

    const dot = card.querySelector('[data-status-dot]');
    if (dot) dot.className = `status-dot status-${name}`;

    const pill = card.querySelector('[data-status-pill]');
    if (pill) {
        pill.className  = `meta-pill status-pill-${name}`;
        pill.textContent = label;
    }

    if (status === 2) {       // Connected — refresh "last connected" timestamp now
        const ts = card.querySelector('[data-last-connected]');
        if (ts) {
            ts.dataset.lastConnected = new Date().toISOString();
            ts.textContent = t('WhatsApp.Connected.JustNow');
        }
    }
}

/* ─── Relative-time ticker (cheap, runs once a minute) ───────────── */
function updateRelativeTimes() {
    document.querySelectorAll('[data-last-connected]').forEach(el => {
        const iso = el.dataset.lastConnected;
        if (!iso) return;
        el.textContent = t('WhatsApp.Connected.Relative', formatRelative(new Date(iso)));
    });
}

function formatRelative(dt) {
    const diff = (Date.now() - dt.getTime()) / 1000;
    if (diff < 60)        return t('WhatsApp.Time.JustNow');
    if (diff < 3600)      return t('WhatsApp.Time.MinutesAgo', Math.floor(diff / 60));
    if (diff < 86400)     return t('WhatsApp.Time.HoursAgo', Math.floor(diff / 3600));
    return t('WhatsApp.Time.DaysAgo', Math.floor(diff / 86400));
}

setInterval(updateRelativeTimes, 60_000);

/* ─── Add modal ─────────────────────────────────────────────────── */
let isCreating = false;     // hard guard against double-submit (any input source)

function openAddModal() {
    document.getElementById('addModal').classList.remove('d-none');
    document.getElementById('step1').classList.remove('d-none');
    document.getElementById('step2').classList.add('d-none');
    document.getElementById('step3').classList.add('d-none');
    document.getElementById('displayNameInput').value = '';
    clearCreateError();
    setCreateButtonState('idle');
    setTimeout(() => document.getElementById('displayNameInput').focus(), 60);
}

function closeAddModal() {
    if (isCreating) return;     // can't close while a request is in flight
    stopPolling();
    document.getElementById('addModal').classList.add('d-none');
    creatingInstanceId = null;
}

async function submitCreate() {
    if (isCreating) return;     // already submitting — ignore extra clicks/keystrokes

    const input = document.getElementById('displayNameInput');
    const name = input.value.trim();

    if (!name) {
        showCreateError(t('WhatsApp.Validation.NameRequired'));
        input.focus();
        return;
    }

    isCreating = true;
    clearCreateError();
    setCreateButtonState('loading');

    try {
        const resp = await fetch('/api/instances', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token()
            },
            body: JSON.stringify({ displayName: name })
        });

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            const msg = resp.status === 409
                ? (err.error ?? t('WhatsApp.Error.Conflict'))
                : (err.error ?? t('WhatsApp.Error.CreateFailed', resp.status));
            showCreateError(msg);
            return;
        }

        const created = await resp.json();
        creatingInstanceId = created.id;

        document.getElementById('step1').classList.add('d-none');
        document.getElementById('step2').classList.remove('d-none');

        await fetchQr();
        startStatusPolling();
    } catch (e) {
        showCreateError(t('WhatsApp.Error.NetworkError', e.message));
    } finally {
        isCreating = false;
        setCreateButtonState('idle');
    }
}

function setCreateButtonState(state) {
    const btn = document.getElementById('submitCreateBtn');
    const cancel = document.getElementById('cancelCreateBtn');
    const input = document.getElementById('displayNameInput');
    const label = btn?.querySelector('.btn-label');
    const spinner = btn?.querySelector('.btn-spinner');
    if (!btn || !label || !spinner) return;

    if (state === 'loading') {
        btn.disabled = true;
        if (cancel) cancel.disabled = true;
        if (input) input.disabled = true;
        label.textContent = t('WhatsApp.Step.Creating');
        spinner.classList.remove('d-none');
    } else {
        btn.disabled = false;
        if (cancel) cancel.disabled = false;
        if (input) input.disabled = false;
        label.textContent = t('Action.Next');
        spinner.classList.add('d-none');
    }
}

function showCreateError(msg) {
    const el = document.getElementById('createError');
    if (!el) return;
    el.textContent = msg;
    el.classList.remove('d-none');
}

function clearCreateError() {
    document.getElementById('createError')?.classList.add('d-none');
}

// Submit on Enter from the input — and dedupe via the same isCreating guard.
document.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    if (document.activeElement?.id !== 'displayNameInput') return;
    e.preventDefault();
    submitCreate();
});

async function fetchQr() {
    if (!creatingInstanceId) return;
    const box = document.getElementById('qrBox');
    box.innerHTML = '<div class="spinner"></div>';

    try {
        const resp = await fetch(`/api/instances/${creatingInstanceId}/qr`);
        if (!resp.ok) {
            box.innerHTML = `<div style="color:#ef4444;font-size:13px;text-align:center;">${escapeHtmlSimple(t('WhatsApp.QR.LoadFailed'))}</div>`;
            return;
        }
        const data = await resp.json();

        if (data.status === 2) {
            showSuccess();
            return;
        }

        if (data.qrBase64) {
            const src = data.qrBase64.startsWith('data:') ? data.qrBase64 : `data:image/png;base64,${data.qrBase64}`;
            box.innerHTML = `<img src="${src}" alt="QR" />`;
        } else {
            box.innerHTML = `<div style="color:#94a3b8;font-size:13px;text-align:center;">${escapeHtmlSimple(t('WhatsApp.QR.NotReady'))}</div>`;
        }
    } catch (e) {
        box.innerHTML = '<div style="color:#ef4444;font-size:13px;text-align:center;">' + escapeHtmlSimple(e.message) + '</div>';
    }
}

function escapeHtmlSimple(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function refreshQr() { fetchQr(); }

function startStatusPolling() {
    stopPolling();
    // Only stop polling on success (status 2 = Connected). "Disconnected" is a normal
    // interim state during reconnect, so we ignore it and keep polling until the user
    // closes the modal.
    statusPollHandle = setInterval(async () => {
        if (!creatingInstanceId) return stopPolling();
        try {
            const resp = await fetch(`/api/instances/${creatingInstanceId}/status`);
            if (!resp.ok) return;
            const data = await resp.json();

            if (data.status === 2) {
                showSuccess(data);
                stopPolling();
            }
        } catch { /* ignore transient errors */ }
    }, 3000);
}

function stopPolling() {
    if (statusPollHandle) { clearInterval(statusPollHandle); statusPollHandle = null; }
    if (qrPollHandle)     { clearInterval(qrPollHandle);     qrPollHandle = null; }
}

function showSuccess(data) {
    document.getElementById('step2').classList.add('d-none');
    document.getElementById('step3').classList.remove('d-none');
    if (data?.phoneNumber) {
        document.getElementById('successDetails').textContent =
            t('WhatsApp.Success.Linked', data.phoneNumber);
    }
}

/* ─── Show QR for an existing pending instance ───────────────────── */
function openQr(instanceId) {
    creatingInstanceId = instanceId;
    document.getElementById('addModal').classList.remove('d-none');
    document.getElementById('step1').classList.add('d-none');
    document.getElementById('step2').classList.remove('d-none');
    document.getElementById('step3').classList.add('d-none');
    fetchQr();
    startStatusPolling();
}

/* ─── Disconnect ─────────────────────────────────────────────────── */
function openDisconnect(id, name) {
    disconnectTargetId = id;
    const titleEl = document.getElementById('disconnectTitle');
    if (titleEl) titleEl.textContent = t('WhatsApp.Disconnect.Title', name);
    document.getElementById('disconnectModal').classList.remove('d-none');
}

function closeDisconnect() {
    disconnectTargetId = null;
    document.getElementById('disconnectModal').classList.add('d-none');
}

async function confirmDisconnect() {
    if (!disconnectTargetId) return;
    try {
        const resp = await fetch(`/api/instances/${disconnectTargetId}/disconnect`, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token() }
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            alert(err.error ?? t('WhatsApp.Disconnect.Failed'));
            return;
        }
        closeDisconnect();
        location.reload();
    } catch (e) {
        alert(t('WhatsApp.Error.NetworkError', e.message));
    }
}

/* ─── Delete (permanent) ─────────────────────────────────────────── */
let deleteTargetId = null;

function openDelete(id, name) {
    deleteTargetId = id;
    const titleEl = document.getElementById('deleteTitle');
    if (titleEl) titleEl.textContent = t('WhatsApp.Delete.Title', name);
    document.getElementById('deleteConfirmInput').value = '';
    document.getElementById('deleteConfirmBtn').disabled = true;
    document.getElementById('deleteModal').classList.remove('d-none');
    setTimeout(() => document.getElementById('deleteConfirmInput').focus(), 60);
}

function closeDelete() {
    deleteTargetId = null;
    document.getElementById('deleteModal').classList.add('d-none');
}

function updateDeleteButton() {
    const text = document.getElementById('deleteConfirmInput').value.trim();
    document.getElementById('deleteConfirmBtn').disabled = text !== 'DELETE';
}

async function confirmDelete() {
    if (!deleteTargetId) return;
    const btn = document.getElementById('deleteConfirmBtn');
    btn.disabled = true;
    btn.textContent = t('WhatsApp.Delete.Pending');

    try {
        const resp = await fetch(`/api/instances/${deleteTargetId}`, {
            method: 'DELETE',
            headers: { 'RequestVerificationToken': token() }
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            alert(err.error ?? t('WhatsApp.Delete.Failed'));
            btn.disabled = false;
            btn.textContent = t('WhatsApp.Delete.Action');
            return;
        }
        closeDelete();
        location.reload();
    } catch (e) {
        alert(t('WhatsApp.Error.NetworkError', e.message));
        btn.disabled = false;
        btn.textContent = t('WhatsApp.Delete.Action');
    }
}
