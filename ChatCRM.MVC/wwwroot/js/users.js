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

const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
const state = { search: '', editingId: null };

/* ─── Toasts ──────────────────────────────────────────────────────── */
function showToast(message, type = 'info') {
    const stack = document.getElementById('toastStack');
    if (!stack) return;
    const el = document.createElement('div');
    el.className = `toast toast-${type}`;
    el.textContent = message;
    stack.appendChild(el);
    setTimeout(() => { el.classList.add('toast-out'); setTimeout(() => el.remove(), 200); }, 2400);
}

function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

function fmtDate(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString(window.__i18nCulture__ || undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

function avatarInitials(text) {
    if (!text) return '?';
    const parts = text.trim().split(/\s+/).slice(0, 2);
    return parts.map(p => p.charAt(0).toUpperCase()).join('') || '?';
}

/* ─── List ────────────────────────────────────────────────────────── */
async function loadUsers() {
    const tbody = document.getElementById('usersBody');
    tbody.innerHTML = `<tr><td colspan="6" class="contacts-empty">${escapeHtml(t('Action.Loading'))}</td></tr>`;

    try {
        const url = state.search ? `/api/users?search=${encodeURIComponent(state.search)}` : '/api/users';
        const resp = await fetch(url);
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        const items = await resp.json();
        renderUsers(items);
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="6" class="contacts-empty">${escapeHtml(t('Users.LoadFailed', err.message))}</td></tr>`;
    }
}

function renderUsers(items) {
    const tbody = document.getElementById('usersBody');
    if (!items.length) {
        tbody.innerHTML = `<tr><td colspan="6" class="contacts-empty">${escapeHtml(t('Users.NoMatch'))}</td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(u => {
        const fullName = [u.firstName, u.lastName].filter(Boolean).join(' ') || u.email;
        const initials = avatarInitials(fullName);
        const role = u.roles?.[0] ?? '';
        const roleCls = role ? `role-${role.toLowerCase()}` : 'role-none';
        const roleLabel = role || t('Users.NoRole');

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
                    ${escapeHtml(u.isActive ? t('Users.Status.Active') : t('Users.Status.Inactive'))}
                </span>
            </td>
            <td class="cell-time-only">${fmtDate(u.createdAt)}</td>
            <td class="cell-actions">
                <button class="btn-ghost" type="button" data-action="edit" data-id="${u.id}">${escapeHtml(t('Users.Action.Edit'))}</button>
                <button class="btn-ghost" type="button" data-action="toggle" data-id="${u.id}" data-active="${u.isActive}">
                    ${escapeHtml(u.isActive ? t('Users.Action.Deactivate') : t('Users.Action.Activate'))}
                </button>
                <button class="btn-ghost btn-danger" type="button" data-action="delete" data-id="${u.id}" data-name="${escapeHtml(fullName)}">${escapeHtml(t('Users.Action.Delete'))}</button>
            </td>
        </tr>`;
    }).join('');
}

/* ─── Modal — create / edit ───────────────────────────────────────── */
function openCreateModal() {
    state.editingId = null;
    document.getElementById('userModalTitle').textContent = t('Users.Modal.AddTitle');
    document.getElementById('userFirstName').value = '';
    document.getElementById('userLastName').value = '';
    document.getElementById('userEmail').value = '';
    document.getElementById('userEmail').disabled = false;
    document.getElementById('userPassword').value = '';
    document.getElementById('userPassword').required = true;
    document.getElementById('passwordLabel').innerHTML = `${escapeHtml(t('Users.Field.Password'))} <em>*</em>`;
    document.getElementById('passwordHint').textContent = t('Users.Hint.Password');
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
    document.getElementById('userModalTitle').textContent = t('Users.Modal.EditTitle');

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
        document.getElementById('passwordLabel').innerHTML = `${escapeHtml(t('Users.Field.NewPassword'))} <small style="color:var(--text-muted);font-weight:400">${escapeHtml(t('Users.Field.NewPasswordOptional'))}</small>`;
        document.getElementById('passwordHint').textContent = t('Users.Hint.NewPassword');
        document.getElementById('userRole').value      = u.roles?.[0] ?? 'Agent';
        document.getElementById('userIsActive').checked = u.isActive;
    } catch (err) {
        showToast(t('Users.Toast.LoadFailed', err.message), 'error');
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

    if (!payload.email) { showUserError(t('Users.Validation.EmailRequired')); return; }

    btn.disabled = true;
    spinner.classList.remove('d-none');
    labelEl.textContent = t('Action.Saving');

    try {
        let resp;
        if (state.editingId) {
            resp = await fetch(`/api/users/${encodeURIComponent(state.editingId)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ ...payload, newPassword: pwd || null })
            });
        } else {
            if (!pwd) { showUserError(t('Users.Validation.PasswordRequired')); btn.disabled = false; spinner.classList.add('d-none'); labelEl.textContent = t('Action.Save'); return; }
            resp = await fetch('/api/users', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
                body: JSON.stringify({ ...payload, password: pwd })
            });
        }

        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showUserError(err.error || t('Users.Toast.SaveFailed'));
            return;
        }

        showToast(state.editingId ? t('Users.Toast.Updated') : t('Users.Toast.Created'), 'success');
        closeUserModal();
        loadUsers();
    } catch (err) {
        showUserError(t('Users.Toast.NetworkError', err.message));
    } finally {
        btn.disabled = false;
        spinner.classList.add('d-none');
        labelEl.textContent = t('Action.Save');
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
                showToast(err.error || t('Users.Toast.Failed'), 'error');
                return;
            }
            showToast(next ? t('Users.Toast.Activated') : t('Users.Toast.Deactivated'), 'success');
            loadUsers();
        } catch (err) { showToast(t('Users.Toast.NetworkError', err.message), 'error'); }
    }

    if (action === 'delete') {
        const name = btn.dataset.name || t('Users.ThisUser');
        openConfirm({
            title: t('Users.Confirm.Delete.Title'),
            body: t('Users.Confirm.Delete.Body', escapeHtml(name)),
            confirmLabel: t('Users.Confirm.Delete.Action'),
            isDanger: true,
            onConfirm: async () => {
                const resp = await fetch(`/api/users/${encodeURIComponent(id)}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': token() }
                });
                if (!resp.ok) {
                    const err = await resp.json().catch(() => ({}));
                    showToast(err.error || t('Users.Toast.Failed'), 'error');
                    return;
                }
                showToast(t('Users.Toast.Deleted'), 'success');
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
