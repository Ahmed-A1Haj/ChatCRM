/* ─── State ──────────────────────────────────────────────────────── */
let creatingInstanceId = null;
let qrPollHandle = null;
let statusPollHandle = null;
let disconnectTargetId = null;

const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

/* ─── SignalR — listen for status changes ───────────────────────── */
const hub = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .withAutomaticReconnect()
    .build();

hub.on('InstanceStatusChanged', ({ id, status }) => {
    const card = document.querySelector(`.instance-card[data-id="${id}"]`);
    if (card) {
        // Refresh page on meaningful status changes to keep UI in sync
        location.reload();
    }
});

hub.start().catch(err => console.error('SignalR error:', err));

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
        showCreateError('Please enter a display name.');
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
                ? (err.error ?? 'A number with this name already exists.')
                : (err.error ?? `Failed to create instance (HTTP ${resp.status}).`);
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
        showCreateError('Network error: ' + e.message);
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
        label.textContent = 'Creating…';
        spinner.classList.remove('d-none');
    } else {
        btn.disabled = false;
        if (cancel) cancel.disabled = false;
        if (input) input.disabled = false;
        label.textContent = 'Next →';
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
            box.innerHTML = '<div style="color:#ef4444;font-size:13px;text-align:center;">Failed to load QR</div>';
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
            box.innerHTML = '<div style="color:#94a3b8;font-size:13px;text-align:center;">QR not ready — tap Refresh.</div>';
        }
    } catch (e) {
        box.innerHTML = '<div style="color:#ef4444;font-size:13px;text-align:center;">' + e.message + '</div>';
    }
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
            `Linked as +${data.phoneNumber}`;
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
    document.getElementById('disconnectName').textContent = name;
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
            alert(err.error ?? 'Failed to disconnect.');
            return;
        }
        closeDisconnect();
        location.reload();
    } catch (e) {
        alert('Network error: ' + e.message);
    }
}

/* ─── Delete (permanent) ─────────────────────────────────────────── */
let deleteTargetId = null;

function openDelete(id, name) {
    deleteTargetId = id;
    document.getElementById('deleteName').textContent = name;
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
    btn.textContent = 'Deleting…';

    try {
        const resp = await fetch(`/api/instances/${deleteTargetId}`, {
            method: 'DELETE',
            headers: { 'RequestVerificationToken': token() }
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            alert(err.error ?? 'Failed to delete.');
            btn.disabled = false;
            btn.textContent = 'Delete forever';
            return;
        }
        closeDelete();
        location.reload();
    } catch (e) {
        alert('Network error: ' + e.message);
        btn.disabled = false;
        btn.textContent = 'Delete forever';
    }
}
