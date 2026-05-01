/* ─── Constants ──────────────────────────────────────────────────── */
const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
const BUILTIN = new Set(['Admin', 'Manager', 'Agent']);

const ROLE_VISUALS = {
    Admin:   { icon: 'shield',    accent: 'role-accent-red'    },
    Manager: { icon: 'briefcase', accent: 'role-accent-amber'  },
    Agent:   { icon: 'headset',   accent: 'role-accent-indigo' }
};
const DEFAULT_VISUAL = { icon: 'lock', accent: 'role-accent-slate' };

const PERMISSION_LABELS = window.__PERMISSION_LABELS__ ?? {};
const PERMISSION_GROUPS = window.__PERMISSION_GROUPS__ ?? {};
const ACTIVE_ROLES      = new Set(window.__ACTIVE_ROLES__ ?? []);

const VISIBLE_PERMS_LIMIT = 6;

/* ─── State ──────────────────────────────────────────────────────── */
const state = {
    editingId: null,
    search: '',
    filter: 'all',         // all | builtin | custom
    sort: 'name-asc',
    cache: []              // last fetched roles
};

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

/* ─── Helpers ────────────────────────────────────────────────────── */
function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c]));
}

const ICONS = {
    'shield':       '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
    'briefcase':    '<rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 7V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2"/>',
    'headset':      '<path d="M3 18v-6a9 9 0 0 1 18 0v6"/><path d="M21 19a2 2 0 0 1-2 2h-1v-7h3z"/><path d="M3 19a2 2 0 0 0 2 2h1v-7H3z"/>',
    'lock':         '<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
    'more':         '<circle cx="12" cy="12" r="1"/><circle cx="12" cy="5" r="1"/><circle cx="12" cy="19" r="1"/>',
    'check':        '<polyline points="20 6 9 17 4 12"/>',
    'plus':         '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>',
    'arrow-right':  '<line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>',
    'eye':          '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>',
    'edit':         '<path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4z"/>',
    'trash':        '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>',
    'inbox-empty':  '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/><path d="M5.45 5.11L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>'
};

function svg(name, size = 14) {
    return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name] ?? ''}</svg>`;
}

function permLabel(key) {
    return PERMISSION_LABELS[key] ?? key;
}

function visualForRole(name) {
    return ROLE_VISUALS[name] ?? DEFAULT_VISUAL;
}

/* ─── Fetch ──────────────────────────────────────────────────────── */
async function loadRoles() {
    const list = document.getElementById('rolesList');
    list.innerHTML = '<div class="contacts-empty">Loading…</div>';

    try {
        const resp = await fetch('/api/roles');
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        state.cache = await resp.json();
        applyFiltersAndRender();
    } catch (err) {
        list.innerHTML = `<div class="contacts-empty">Failed to load: ${escapeHtml(err.message)}</div>`;
    }
}

function applyFiltersAndRender() {
    let items = [...state.cache];

    // Filter by kind
    if (state.filter === 'builtin') items = items.filter(r => BUILTIN.has(r.name));
    else if (state.filter === 'custom') items = items.filter(r => !BUILTIN.has(r.name));

    // Search by role name OR permission keys/labels
    if (state.search) {
        const q = state.search.toLowerCase();
        items = items.filter(r => {
            if (r.name.toLowerCase().includes(q)) return true;
            return r.permissions.some(p =>
                p.toLowerCase().includes(q) || (PERMISSION_LABELS[p] ?? '').toLowerCase().includes(q)
            );
        });
    }

    // Sort
    const cmp = sortComparator(state.sort);
    items.sort(cmp);

    renderRoles(items);
}

function sortComparator(sort) {
    switch (sort) {
        case 'name-asc':   return (a, b) => a.name.localeCompare(b.name);
        case 'name-desc':  return (a, b) => b.name.localeCompare(a.name);
        case 'users-desc': return (a, b) => (b.userCount - a.userCount) || a.name.localeCompare(b.name);
        case 'users-asc':  return (a, b) => (a.userCount - b.userCount) || a.name.localeCompare(b.name);
        case 'perms-desc': return (a, b) => (b.permissions.length - a.permissions.length) || a.name.localeCompare(b.name);
        case 'perms-asc':  return (a, b) => (a.permissions.length - b.permissions.length) || a.name.localeCompare(b.name);
        default:           return (a, b) => a.name.localeCompare(b.name);
    }
}

/* ─── Render ─────────────────────────────────────────────────────── */
function renderRoles(items) {
    const list = document.getElementById('rolesList');

    if (!state.cache.length) {
        list.innerHTML = renderEmptyAllRoles();
        return;
    }

    if (!items.length) {
        list.innerHTML = `
            <div class="contacts-empty empty-state">
                <div class="empty-icon">${svg('lock', 32)}</div>
                <p class="empty-title">No roles match your filter</p>
                <p class="empty-sub">Try a different search or clear the filter.</p>
            </div>`;
        return;
    }

    list.innerHTML = items.map(renderCard).join('');
}

function renderEmptyAllRoles() {
    return `
        <div class="contacts-empty empty-state empty-state-full">
            <div class="empty-icon">${svg('inbox-empty', 40)}</div>
            <p class="empty-title">No roles yet</p>
            <p class="empty-sub">Create your first custom role to fine-tune access for your team.</p>
            <button class="btn-primary-soft has-icon" type="button" onclick="openCreateRole()">
                ${svg('plus', 14)}<span>Add role</span>
            </button>
        </div>`;
}

function renderCard(r) {
    const isBuiltin = BUILTIN.has(r.name);
    const isMyRole  = ACTIVE_ROLES.has(r.name);
    const visual    = visualForRole(r.name);

    const perms = r.permissions;
    const visiblePerms = perms.slice(0, VISIBLE_PERMS_LIMIT);
    const hiddenCount = Math.max(0, perms.length - VISIBLE_PERMS_LIMIT);

    const visibleHtml = visiblePerms.length
        ? visiblePerms.map(p =>
            `<span class="role-perm-chip" title="${escapeHtml(p)}">${escapeHtml(permLabel(p))}</span>`
          ).join('')
        : '<span class="role-perm-empty">No permissions assigned</span>';

    const moreBtn = hiddenCount > 0
        ? `<button class="role-perm-more" type="button" data-show-all="${escapeHtml(r.id)}"
                   aria-label="Show ${hiddenCount} more permissions">+${hiddenCount} more</button>`
        : '';

    const userCountLine = r.userCount > 0
        ? `<strong>${r.userCount}</strong> user${r.userCount === 1 ? '' : 's'}`
        : `<a href="/dashboard/settings/users" class="role-cta">Assign users ${svg('arrow-right', 11)}</a>`;

    const editLabel = isBuiltin ? 'Customize' : 'Edit';
    const editIcon  = isBuiltin ? 'eye' : 'edit';

    return `
    <article class="role-card ${visual.accent} ${isMyRole ? 'is-my-role' : ''}" data-id="${escapeHtml(r.id)}" data-name="${escapeHtml(r.name)}" data-builtin="${isBuiltin}">
        <header class="role-card-head">
            <div class="role-card-icon">${svg(visual.icon, 20)}</div>
            <div class="role-card-title">
                <h3>${escapeHtml(r.name)}</h3>
                <div class="role-card-tags">
                    ${isBuiltin ? '<span class="role-tag role-tag-builtin">Built-in</span>' : '<span class="role-tag role-tag-custom">Custom</span>'}
                    ${isMyRole ? '<span class="role-tag role-tag-mine">Your role</span>' : ''}
                </div>
            </div>
        </header>

        <p class="role-card-meta">
            ${userCountLine} · <strong>${perms.length}</strong> permission${perms.length === 1 ? '' : 's'}
        </p>

        <div class="role-perms" data-role-id="${escapeHtml(r.id)}">
            <div class="role-perms-visible">${visibleHtml}${moreBtn}</div>
            <div class="role-perms-extra d-none">
                ${perms.slice(VISIBLE_PERMS_LIMIT).map(p =>
                    `<span class="role-perm-chip" title="${escapeHtml(p)}">${escapeHtml(permLabel(p))}</span>`
                ).join('')}
            </div>
        </div>

        <footer class="role-card-actions">
            <button class="btn-ghost has-icon" type="button" data-action="edit" data-id="${escapeHtml(r.id)}" aria-label="${editLabel} ${escapeHtml(r.name)}">
                ${svg(editIcon, 13)}<span>${editLabel}</span>
            </button>

            ${isBuiltin ? '' : renderKebab(r.id, r.name)}
        </footer>
    </article>`;
}

function renderKebab(id, name) {
    return `
    <div class="kebab-wrap">
        <button class="btn-icon-square" type="button" data-action="kebab" data-id="${escapeHtml(id)}"
                aria-haspopup="true" aria-expanded="false" aria-label="More actions for ${escapeHtml(name)}">
            ${svg('more', 14)}
        </button>
    </div>`;
}

/* ─── Permission overflow toggle ──────────────────────────────────── */
document.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-show-all]');
    if (!btn) return;
    const card = btn.closest('.role-card');
    const extra = card?.querySelector('.role-perms-extra');
    if (!extra) return;
    extra.classList.remove('d-none');
    btn.remove();
});

/* ─── Kebab popover for delete ────────────────────────────────────── */
document.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-action="kebab"]');
    if (!btn) {
        if (!e.target.closest('.kebab-popover')) closeKebab();
        return;
    }

    const wrap = btn.parentElement;
    const existing = wrap.querySelector('.kebab-popover');
    closeKebab();
    if (existing) return; // toggle off

    btn.setAttribute('aria-expanded', 'true');
    const card = btn.closest('.role-card');
    const id   = card.dataset.id;
    const name = card.dataset.name;

    wrap.insertAdjacentHTML('beforeend', `
        <div class="kebab-popover" role="menu" aria-label="Role actions">
            <button class="kebab-item is-danger" type="button" data-confirm-delete data-id="${escapeHtml(id)}" data-name="${escapeHtml(name)}" role="menuitem">
                ${svg('trash', 13)}<span>Delete role</span>
            </button>
        </div>`);
});

function closeKebab() {
    document.querySelectorAll('.kebab-popover').forEach(p => p.remove());
    document.querySelectorAll('button[data-action="kebab"][aria-expanded="true"]')
        .forEach(b => b.setAttribute('aria-expanded', 'false'));
}

/* Esc closes kebab + modals */
document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    closeKebab();
    if (!document.getElementById('roleModal')?.classList.contains('d-none'))    closeRoleModal();
    if (!document.getElementById('confirmModal')?.classList.contains('d-none')) closeConfirmModal();
});

/* ─── Card actions: edit / delete ─────────────────────────────────── */
document.addEventListener('click', async (e) => {
    const editBtn = e.target.closest('button[data-action="edit"]');
    if (editBtn) return openEditRole(editBtn.dataset.id);

    const delBtn = e.target.closest('button[data-confirm-delete]');
    if (delBtn) {
        const id = delBtn.dataset.id;
        const name = delBtn.dataset.name;
        const role = state.cache.find(r => r.id === id);
        const userCount = role?.userCount ?? 0;
        closeKebab();

        const body = userCount > 0
            ? `<strong>${escapeHtml(name)}</strong> currently has <strong>${userCount}</strong> user${userCount === 1 ? '' : 's'} assigned. Reassign them before deleting.`
            : `<strong>${escapeHtml(name)}</strong> will be permanently removed.`;

        openConfirm({
            title: 'Delete role?',
            body,
            confirmLabel: userCount > 0 ? 'Cannot delete' : 'Delete',
            isDanger: true,
            disabled: userCount > 0,
            onConfirm: async () => {
                const resp = await fetch(`/api/roles/${encodeURIComponent(id)}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': token() }
                });
                if (!resp.ok) {
                    const err = await resp.json().catch(() => ({}));
                    showToast(err.error || 'Failed.', 'error');
                    return;
                }
                showToast('Role deleted', 'success');
                loadRoles();
            }
        });
    }
});

/* ─── Modal — create / edit ───────────────────────────────────────── */
function openCreateRole() {
    state.editingId = null;
    document.getElementById('roleModalTitle').textContent = 'Add role';
    document.getElementById('roleName').value = '';
    document.getElementById('roleName').disabled = false;
    document.getElementById('roleNameHint').textContent = '';
    document.querySelectorAll('input[data-permission]').forEach(cb => cb.checked = false);
    clearRoleError();
    document.getElementById('roleModal').classList.remove('d-none');
    setTimeout(() => document.getElementById('roleName').focus(), 60);
}
window.openCreateRole = openCreateRole;

async function openEditRole(id) {
    state.editingId = id;
    clearRoleError();
    document.getElementById('roleModal').classList.remove('d-none');

    try {
        const resp = await fetch(`/api/roles/${encodeURIComponent(id)}`);
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        const r = await resp.json();
        const isBuiltin = BUILTIN.has(r.name);

        document.getElementById('roleModalTitle').textContent = isBuiltin ? `Customize "${r.name}"` : `Edit "${r.name}"`;
        document.getElementById('roleName').value = r.name;
        document.getElementById('roleName').disabled = isBuiltin;
        document.getElementById('roleNameHint').textContent = isBuiltin
            ? "Built-in role names can't be changed, but you can adjust their permissions."
            : '';

        const perms = new Set(r.permissions);
        document.querySelectorAll('input[data-permission]').forEach(cb => {
            cb.checked = perms.has(cb.dataset.permission);
        });
    } catch (err) {
        showToast('Failed to load role: ' + err.message, 'error');
        closeRoleModal();
    }
}

function closeRoleModal() {
    document.getElementById('roleModal').classList.add('d-none');
    state.editingId = null;
}
window.closeRoleModal = closeRoleModal;

function showRoleError(msg) {
    const el = document.getElementById('roleFormError');
    el.textContent = msg;
    el.classList.remove('d-none');
}
function clearRoleError() {
    document.getElementById('roleFormError').classList.add('d-none');
}

document.getElementById('roleSaveBtn').addEventListener('click', async () => {
    const btn = document.getElementById('roleSaveBtn');
    const spinner = btn.querySelector('.btn-spinner');
    const labelEl = btn.querySelector('.btn-label');

    const name = document.getElementById('roleName').value.trim();
    if (!name) { showRoleError('Role name is required.'); return; }

    const checked = Array.from(document.querySelectorAll('input[data-permission]:checked'))
        .map(cb => cb.dataset.permission);

    btn.disabled = true;
    spinner.classList.remove('d-none');
    labelEl.textContent = 'Saving…';

    try {
        const resp = await fetch('/api/roles', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify({ id: state.editingId, name, permissions: checked })
        });
        if (!resp.ok) {
            const err = await resp.json().catch(() => ({}));
            showRoleError(err.error || 'Failed to save role.');
            return;
        }
        showToast(state.editingId ? 'Role updated' : 'Role created', 'success');
        closeRoleModal();
        loadRoles();
    } catch (err) {
        showRoleError('Network error: ' + err.message);
    } finally {
        btn.disabled = false;
        spinner.classList.add('d-none');
        labelEl.textContent = 'Save';
    }
});

/* "Toggle all" inside a permission group */
document.addEventListener('click', (e) => {
    const link = e.target.closest('button[data-toggle-group]');
    if (!link) return;
    const group = link.dataset.toggleGroup;
    const checks = document.querySelectorAll(`input[data-permission][data-group="${group}"]`);
    const allChecked = Array.from(checks).every(cb => cb.checked);
    checks.forEach(cb => cb.checked = !allChecked);
});

/* ─── Confirm modal ───────────────────────────────────────────────── */
let pendingConfirm = null;

function openConfirm({ title, body, confirmLabel, isDanger, disabled, onConfirm }) {
    pendingConfirm = onConfirm;
    document.getElementById('confirmTitle').textContent = title;
    document.getElementById('confirmBody').innerHTML = body || '';
    const btn = document.getElementById('confirmAction');
    btn.textContent = confirmLabel || 'Confirm';
    btn.className = 'btn-ghost ' + (isDanger ? 'btn-danger' : '');
    btn.disabled = !!disabled;
    document.getElementById('confirmModal').classList.remove('d-none');
}

function closeConfirmModal() {
    pendingConfirm = null;
    document.getElementById('confirmModal').classList.add('d-none');
    document.getElementById('confirmAction').disabled = false;
}
window.closeConfirmModal = closeConfirmModal;

document.getElementById('confirmAction')?.addEventListener('click', async () => {
    const fn = pendingConfirm;
    closeConfirmModal();
    if (typeof fn === 'function') await fn();
});

/* ─── Toolbar wiring ──────────────────────────────────────────────── */
function debounce(fn, ms) {
    let t;
    return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
}

document.getElementById('roleSearch').addEventListener('input', debounce((e) => {
    state.search = e.target.value.trim();
    applyFiltersAndRender();
}, 200));

document.querySelectorAll('.rt-chip').forEach(chip => {
    chip.addEventListener('click', () => {
        document.querySelectorAll('.rt-chip').forEach(c => {
            c.classList.remove('is-active');
            c.setAttribute('aria-selected', 'false');
        });
        chip.classList.add('is-active');
        chip.setAttribute('aria-selected', 'true');
        state.filter = chip.dataset.filter;
        applyFiltersAndRender();
    });
});

document.getElementById('roleSort').addEventListener('change', (e) => {
    state.sort = e.target.value;
    applyFiltersAndRender();
});

/* ─── Boot ────────────────────────────────────────────────────────── */
document.getElementById('addRoleBtn').addEventListener('click', openCreateRole);
loadRoles();
