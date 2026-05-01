const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
const state = { search: '', editingId: null };

/* ─── Toasts ──────────────────────────────────────────────────────── */
function showToast(message, type = 'info') {
    const stack = document.getElementById('toastStack');
    if (!stack) return;
    const t = document.createElement('div');
    t.className = `toast toast-${type}`;
    t.textContent = message;
    stack.appendChild(t);
    setTimeout(() => { t.classList.add('toast-out'); setTimeout(() => t.remove(), 200); }, 2400);
}

function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function fmtDate(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

function avatarInitials(text) {
    if (!text) return '?';
    const parts = text.trim().split(/\s+/).slice(0, 2);
    return parts.map(p => p.charAt(0).toUpperCase()).join('') || '?';
}

/* ─── List ────────────────────────────────────────────────────────── */
async function loadUsers() {
    const tbody = document.getElementById('usersBody');
    tbody.innerHTML = '<tr><td colspan="6" class="contacts-empty">Loading…</td></tr>';

    try {
        const url = state.search ? `/api/users?search=${encodeURIComponent(state.search)}` : '/api/users';
        const resp = await fetch(url);
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        const items = await resp.json();
        renderUsers(items);
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="6" class="contacts-empty">Failed to load: ${escapeHtml(err.message)}</td></tr>`;
    }
}

function renderUsers(items) {
    const tbody = document.getElementById('usersBody');
    if (!items.length) {
        tbody.innerHTML = '<tr><td colspan="6" class="contacts-empty">No users match.</td></tr>';
        return;
    }

    tbody.innerHTML = items.map(u => {
        const fullName = [u.firstName, u.lastName].filter(Boolean).join(' ') || u.email;
        const initials = avatarInitials(fullName);
        const role = u.roles?.[0] ?? '';
        const roleCls = role ? `role-${role.toLowerCase()}` : 'role-none';
        const roleLabel = role || 'No role';

        return `
        <tr data-id="${u.id}">
            <td>
                <div class="cell-contact">
                    <span class="cell-avatar">${escapeHtml(initials)}</span>
                    <div class="cell-contact-text">
                        <span class="cell-contact-name">${escapeHtml(fullName)}</span>
                    </div>
                </div>
            </td>
            <td><span class="user-cell-email">${escapeHtml(u.email)}</span></td>
            <td><span class="user-role-badge ${roleCls}">${escapeHtml(roleLabel)}</span></td>
            <td>
                <span class="user-status-pill ${u.isActive ? 'active' : 'inactive'}">
                    ${u.isActive ? 'Active' : 'Inactive'}
                </span>
            </td>
            <td class="cell-time-only">${fmtDate(u.createdAt)}</td>
            <td class="cell-actions">
                <button class="btn-ghost" type="button" data-action="edit" data-id="${u.id}">Edit</button>
                <button class="btn-ghost" type="button" data-action="toggle" data-id="${u.id}" data-active="${u.isActive}">
                    ${u.isActive ? 'Deactivate' : 'Activate'}
                </button>
                <button class="btn-ghost btn-danger" type="button" data-action="delete" data-id="${u.id}" data-name="${escapeHtml(fullName)}">Delete</button>
            </td>
        </tr>`;
    }).join('');
}

/* ─── Modal — create / edit ───────────────────────────────────────── */
function openCreateModal() {
    state.editingId = null;
    document.getElementById('userModalTitle').textContent = 'Add user';
    document.getElementById('userFirstName').value = '';
    document.getElementById('userLastName').value = '';
    document.getElementById('userEmail').value = '';
    document.getElementById('userEmail').disabled = false;
    document.getElementById('userPassword').value = '';
    document.getElementById('userPassword').required = true;
    document.getElementById('passwordLabel').innerHTML = 'Password <em>*</em>';
    document.getElementById('passwordHint').textContent = 'Min 10 characters, mixed case, with a digit.';
    document.getElementById('userRole').value = 'Agent';
    document.getElementById('userIsActive').checked = true;
    clearUserError();
    document.getElementById('userModal').classList.remove('d-none');
    setTimeout(() => document.getElementById('userFirstName').focus(), 60);
}

async function openEditModal(id) {
    state.editingId = id;
    clearUserError();
    document.getElementById('userModal').classList.remove('d-none');
    document.getElementById('userModalTitle').textContent = 'Edit user';

    try {
        const resp = await fetch(`/api/users/${encodeURIComponent(id)}`);
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        const u = await resp.json();

        document.getElementById('userFirstName').value = u.firstName ?? '';
        document.getElementById('userLastName').value  = u.lastName ?? '';
        document.getElementById('userEmail').value     = u.email ?? '';
        document.getElementById('userEmail').disabled  = false;
        document.getElementById('userPassword').value  = '';
        document.getElementById('userPassword').required = false;
        document.getElementById('passwordLabel').innerHTML = 'New password <small style="color:var(--text-muted);font-weight:400">(optional)</small>';
        document.getElementById('passwordHint').textContent = 'Leave blank to keep the existing password.';
        document.getElementById('userRole').value      = u.roles?.[0] ?? 'Agent';
        document.getElementById('userIsActive').checked = u.isActive;
    } catch (err) {
        showToast('Failed to load user: ' + err.message, 'error');
        closeUserModal();
    }
}

function closeUserModal() {
    document.getElementById('userModal').classList.add('d-none');
    state.editingId = null;
}
window.closeUserModal = closeUserModal;

function showUserError(msg) {
    const el = document.getElementById('userFormError');
    el.textContent = msg;
    el.classList.remove('d-none');
}
function clearUserError() {
    document.getElementById('userFormError').classList.add('d-none');
}

document.getElementById('userSaveBtn').addEventListener('click', async () => {
    const btn = document.getElementById('userSaveBtn');
    const spinner = btn.querySelector('.btn-spinner');
    const labelEl = btn.querySelector('.btn-label');

    const payload = {
        firstName: document.getElementById('userFirstName').value.trim(),
        lastName:  document.getElementById('userLastName').value.trim(),
        email:     document.getElementById('userEmail').value.trim(),
        role:      document.getElementById('userRole').value,
        isActive:  document.getElementById('userIsActive').checked,
    };
    const pwd = document.getElementById('userPassword').value;

    if (!payload.email) { showUserError('Email is required.'); return; }

    btn.disabled = true;
    spinner.classList.remove('d-none');
    labelEl.textContent = 'Saving…';

    try {
        let resp;
        if (state.editingId) {
            resp = await fetch(`/api/users/${encodeURIComponent(state.editingId)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ ...payload, newPassword: pwd || null })
            });
        } else {
            if (!pwd) { showUserError('Password is required.'); btn.disabled = false; spinner.classList.add('d-none'); labelEl.textContent = 'Save'; return; }
            resp = await fetch('/api/users', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ ...payload, password: pwd })
            });
        }

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showUserError(err.error || 'Failed to save user.');
            return;
        }

        showToast(state.editingId ? 'User updated' : 'User created', 'success');
        closeUserModal();
        loadUsers();
    } catch (err) {
        showUserError('Network error: ' + err.message);
    } finally {
        btn.disabled = false;
        spinner.classList.add('d-none');
        labelEl.textContent = 'Save';
    }
});

/* ─── Row actions ─────────────────────────────────────────────────── */
document.addEventListener('click', async (e) => {
    const btn = e.target.closest('button[data-action]');
    if (!btn) return;
    const id = btn.dataset.id;
    const action = btn.dataset.action;

    if (action === 'edit') return openEditModal(id);

    if (action === 'toggle') {
        const wasActive = btn.dataset.active === 'true';
        const next = !wasActive;
        try {
            const resp = await fetch(`/api/users/${encodeURIComponent(id)}/active`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ isActive: next })
            });
            if (!resp.ok) {
                const err = await resp.json().catch(() => ({}));
                showToast(err.error || 'Failed.', 'error');
                return;
            }
            showToast(next ? 'User activated' : 'User deactivated', 'success');
            loadUsers();
        } catch (err) { showToast('Network error: ' + err.message, 'error'); }
    }

    if (action === 'delete') {
        const name = btn.dataset.name || 'this user';
        openConfirm({
            title: 'Delete user?',
            body: `<strong>${escapeHtml(name)}</strong> will be permanently removed. This cannot be undone.`,
            confirmLabel: 'Delete',
            isDanger: true,
            onConfirm: async () => {
                const resp = await fetch(`/api/users/${encodeURIComponent(id)}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': token() }
                });
                if (!resp.ok) {
                    const err = await resp.json().catch(() => ({}));
                    showToast(err.error || 'Failed.', 'error');
                    return;
                }
                showToast('User deleted', 'success');
                loadUsers();
            }
        });
    }
});

/* ─── Confirm modal helper ────────────────────────────────────────── */
let pendingConfirm = null;

function openConfirm({ title, body, confirmLabel, isDanger, onConfirm }) {
    pendingConfirm = onConfirm;
    document.getElementById('confirmTitle').textContent = title;
    document.getElementById('confirmBody').innerHTML = body || '';
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

/* ─── Boot ────────────────────────────────────────────────────────── */
document.getElementById('addUserBtn').addEventListener('click', openCreateModal);

let searchTimer;
document.getElementById('userSearch').addEventListener('input', (e) => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
        state.search = e.target.value.trim();
        loadUsers();
    }, 250);
});

loadUsers();
