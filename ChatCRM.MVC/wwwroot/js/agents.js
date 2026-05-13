// Agents Management — vibrant cards with default callout, stat tiles, and toggle.
// Self-contained, no framework. Mirrors patterns from templates.js / users.js.
(function () {
    'use strict';

    const caps = window.__caps__ || { canManage: false };

    // Cycled per card for visual variety. Default agents are pinned to emerald;
    // inactive agents are pinned to rose; active non-default rotate through blue/purple/cyan/amber.
    const ACTIVE_PALETTE = ['blue', 'purple', 'cyan', 'amber'];

    let activeTab = 'all';
    let cachedAll = null;
    let pendingDeleteId = null;

    function $(id) { return document.getElementById(id); }
    function csrf() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''; }

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function showToast(message, kind) {
        const el = $('toast');
        if (!el) return;
        el.textContent = message;
        el.classList.remove('is-error', 'is-success', 'd-none');
        if (kind === 'error')   el.classList.add('is-error');
        if (kind === 'success') el.classList.add('is-success');
        clearTimeout(el._hide);
        el._hide = setTimeout(() => el.classList.add('d-none'), 4200);
    }

    function avatarHtml(agent) {
        if (agent.avatarPath) {
            return `<img src="${escapeHtml(agent.avatarPath)}" alt="${escapeHtml(agent.name)}" />`;
        }
        const initial = (agent.name || '🤖').trim().charAt(0).toUpperCase();
        return `<span aria-hidden="true">${escapeHtml(initial)}</span>`;
    }

    function pickAccent(agent, indexAmongActive) {
        if (!agent.isActive) return 'rose';
        if (agent.isDefault) return 'emerald';
        return ACTIVE_PALETTE[indexAmongActive % ACTIVE_PALETTE.length];
    }

    function fmtCount(n) {
        if (n == null) return '0';
        if (n >= 1000) return (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k';
        return String(n);
    }

    // ── List load + render ─────────────────────────────────────────────
    async function load() {
        const params = new URLSearchParams({ filter: activeTab });
        const search = $('searchInput')?.value?.trim();
        if (search) params.set('q', search);

        const resp = await fetch('/dashboard/agents/list?' + params.toString());
        if (!resp.ok) {
            showToast(t('Agents.Toast.LoadFailed'), 'error');
            return;
        }
        const rows = await resp.json();
        renderGrid(rows);

        if (activeTab !== 'all' || search) {
            const totalResp = await fetch('/dashboard/agents/list');
            if (totalResp.ok) {
                cachedAll = await totalResp.json();
                updateCounts(cachedAll);
            }
        } else {
            cachedAll = rows;
            updateCounts(rows);
        }
    }

    function updateCounts(rows) {
        $('countAll').textContent = rows.length;
        $('countActive').textContent = rows.filter(a => a.isActive).length;
        $('countInactive').textContent = rows.filter(a => !a.isActive).length;
    }

    function renderGrid(rows) {
        const grid = $('agentsGrid');
        const empty = $('emptyState');
        grid.innerHTML = '';

        if (!rows.length) {
            empty.classList.remove('d-none');
            $('emptyTitle').textContent = activeTab === 'inactive'
                ? t('Agents.Empty.InactiveTitle')
                : t('Agents.Empty.Title');
            $('emptySub').textContent = activeTab === 'inactive'
                ? t('Agents.Empty.InactiveSub')
                : t('Agents.Empty.Sub');
            return;
        }
        empty.classList.add('d-none');

        let activeIdx = 0;
        rows.forEach(a => {
            grid.appendChild(buildCard(a, a.isActive && !a.isDefault ? activeIdx++ : 0));
        });

        if (caps.canManage) {
            grid.appendChild(buildPlaceholderCard());
        }
    }

    function buildCard(a, paletteIndex) {
        const accent = pickAccent(a, paletteIndex);

        const card = document.createElement('article');
        card.className = 'agent-card accent-' + accent + (a.isActive ? '' : ' is-inactive');
        card.dataset.id = a.id;

        // Top row: status pill + 3-dot menu.
        const statusKlass = a.isActive ? 'is-active' : 'is-inactive';
        const statusLabel = a.isActive ? t('Agents.Status.Active') : t('Agents.Status.Inactive');

        const defaultPill = a.isDefault
            ? `<span class="agent-default-pill"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>${escapeHtml(t('Agents.Status.Default'))}</span>`
            : '';

        const callout = a.isDefault
            ? `<div class="agent-default-callout">
                  <span class="agent-default-callout-icon">✨</span>
                  <span>${escapeHtml(t('Agents.Card.DefaultCallout'))}</span>
               </div>`
            : '';

        const setDefaultBtn = a.isDefault
            ? `<button type="button" class="agent-set-default-btn is-current" disabled>
                  ${starIcon(13)} ${escapeHtml(t('Agents.Status.Default'))}
               </button>`
            : (caps.canManage && a.isActive
                ? `<button type="button" class="agent-set-default-btn" data-act="default">
                      ${starIcon(13)} ${escapeHtml(t('Agents.Action.SetDefault'))}
                   </button>`
                : '');

        const editBtn = caps.canManage
            ? `<button type="button" class="agent-edit-icon-btn" data-act="edit" title="${escapeHtml(t('Agents.Action.Edit'))}" aria-label="${escapeHtml(t('Agents.Action.Edit'))}">
                  ${pencilIcon(14)}
               </button>`
            : '';

        const deleteMenuItem = caps.canManage && !a.isDefault ? `
            <button type="button" class="agent-card-menu-item" data-act="delete" role="menuitem">
                ${trashIcon(13)} ${escapeHtml(t('Agents.Action.Delete'))}
            </button>` : '';

        const toggle = caps.canManage
            ? `<label class="ag-toggle" title="${escapeHtml(a.isActive ? t('Agents.Action.Deactivate') : t('Agents.Action.Activate'))}">
                 <input type="checkbox" ${a.isActive ? 'checked' : ''} ${a.isDefault ? 'disabled' : ''} data-act="toggle" />
                 <span class="ag-switch"><span class="ag-switch-knob"></span></span>
               </label>`
            : '';

        const menuBtn = caps.canManage && !a.isDefault
            ? `<button type="button" class="agent-card-menu" data-act="menu" aria-label="${escapeHtml(t('Agents.Action.More'))}">⋯</button>`
            : '';

        card.innerHTML = `
            <div class="agent-card-body">
                <div class="agent-card-top">
                    <span class="agent-status-pill ${statusKlass}">
                        <span class="agent-status-dot"></span>
                        ${escapeHtml(statusLabel)}
                    </span>
                    ${menuBtn}
                </div>
                <div class="agent-card-avatar">${avatarHtml(a)}</div>
                <div>
                    <div class="agent-card-name-row">
                        <h3 class="agent-card-name">${escapeHtml(a.name)}</h3>
                        ${defaultPill}
                    </div>
                </div>
                <p class="agent-card-desc">${escapeHtml(a.description || t('Agents.Card.NoDescription'))}</p>
                ${callout}
                <div class="agent-card-stats">
                    <div class="agent-stat">
                        <div class="agent-stat-row">
                            ${userIcon(13)}
                            <span class="agent-stat-value">${escapeHtml(fmtCount(a.assignedContactsCount))}</span>
                        </div>
                        <span class="agent-stat-label">${escapeHtml(t('Agents.Card.Contacts'))}</span>
                    </div>
                    <div class="agent-stat">
                        <div class="agent-stat-row">
                            ${chatIcon(13)}
                            <span class="agent-stat-value">${escapeHtml(fmtCount(a.assignedConversationsCount))}</span>
                        </div>
                        <span class="agent-stat-label">${escapeHtml(t('Agents.Card.Conversations'))}</span>
                    </div>
                </div>
            </div>
            <div class="agent-card-foot">
                <div class="agent-card-foot-left">${setDefaultBtn}</div>
                <div class="agent-card-foot-actions">
                    ${editBtn}
                    ${toggle}
                </div>
            </div>
        `;

        // Wire actions.
        card.querySelectorAll('[data-act]').forEach(el => {
            const act = el.dataset.act;
            if (act === 'toggle') {
                el.addEventListener('change', e => handleToggle(a, e.target.checked, e.target));
            } else {
                el.addEventListener('click', e => {
                    e.stopPropagation();
                    handleAction(act, a);
                });
            }
        });

        return card;
    }

    function buildPlaceholderCard() {
        const card = document.createElement('button');
        card.type = 'button';
        card.className = 'agent-card-placeholder';
        card.innerHTML = `
            <h3>${escapeHtml(t('Agents.Placeholder.Title'))}</h3>
            <p>${escapeHtml(t('Agents.Placeholder.Sub'))}</p>
            <span class="agent-card-placeholder-btn">+ ${escapeHtml(t('Agents.CreateBtn'))}</span>
        `;
        card.addEventListener('click', () => openEditor(null));
        return card;
    }

    async function handleAction(act, a) {
        if (act === 'edit')    return openEditor(a.id);
        if (act === 'default') return setDefault(a.id);
        if (act === 'delete')  return openDeleteModal(a);
        if (act === 'menu')    return openDeleteModal(a); // 3-dot menu currently maps to delete
    }

    async function handleToggle(a, isActive, input) {
        if (a.isDefault && !isActive) {
            // Default agents can't be deactivated. Restore the visual state and toast.
            input.checked = true;
            showToast(t('Agents.Error.DefaultMustBeActive'), 'error');
            return;
        }
        const r = await fetchJson(`/dashboard/agents/${a.id}/active`, {
            method: 'POST',
            body: JSON.stringify({ isActive })
        });
        if (r.ok) {
            showToast(isActive ? t('Agents.Toast.Activated') : t('Agents.Toast.Deactivated'), 'success');
            load();
        } else {
            showToast(r.payload?.error || t('Agents.Toast.UpdateFailed'), 'error');
            load();
        }
    }

    async function setDefault(id) {
        const r = await fetchJson(`/dashboard/agents/${id}/default`, { method: 'POST', body: '{}' });
        if (r.ok) { showToast(t('Agents.Toast.DefaultSet'), 'success'); load(); }
        else      { showToast(r.payload?.error || t('Agents.Toast.UpdateFailed'), 'error'); }
    }

    // ── Editor ────────────────────────────────────────────────────────
    async function openEditor(id) {
        const isCreate = !id;
        clearEditorErrors();
        $('editTitle').textContent = isCreate ? t('Agents.Editor.Title') : t('Agents.Editor.EditTitle');
        $('editId').value = id || '';
        $('avatarFile').value = '';

        if (isCreate) {
            $('editName').value = '';
            $('editDescription').value = '';
            $('editInstructions').value = '';
            updateInstructionsCount();
            $('editIsActive').checked = true;
            $('editIsActive').disabled = false;
            $('editMakeDefault').checked = false;
            renderAvatarPreview(null);
            $('makeDefaultRow').style.display = '';
        } else {
            const r = await fetchJson(`/dashboard/agents/${id}`);
            if (!r.ok) { showToast(t('Agents.Toast.LoadFailed'), 'error'); return; }
            const a = r.payload;
            $('editName').value = a.name;
            $('editDescription').value = a.description || '';
            $('editInstructions').value = a.instructions || '';
            updateInstructionsCount();
            $('editIsActive').checked = a.isActive;
            // Default agents must stay active — block the active toggle so the user can't try
            // to demote and deactivate in one step. To demote: uncheck "default" first.
            $('editIsActive').disabled = a.isDefault;
            // Always show the default toggle. For the current default it's pre-checked —
            // unchecking demotes (workspace ends up with no default until another is promoted).
            $('editMakeDefault').checked = a.isDefault;
            $('makeDefaultRow').style.display = '';
            renderAvatarPreview(a.avatarPath);
        }

        $('editModal').classList.remove('d-none');
        $('editName').focus();
    }

    function closeEditor() {
        $('editModal').classList.add('d-none');
        $('editIsActive').disabled = false;
    }

    function renderAvatarPreview(path) {
        const img = $('avatarImg');
        const placeholder = $('avatarPlaceholder');
        if (path) {
            img.src = path;
            img.classList.remove('d-none');
            placeholder.classList.add('d-none');
        } else {
            img.classList.add('d-none');
            placeholder.classList.remove('d-none');
        }
    }

    function updateInstructionsCount() {
        const ta = $('editInstructions');
        const out = $('instructionsCount');
        if (ta && out) out.textContent = (ta.value || '').length;
    }

    function clearEditorErrors() {
        document.querySelectorAll('.ag-error[data-error-for]').forEach(e => e.textContent = '');
        document.querySelectorAll('.ag-field.has-error').forEach(f => f.classList.remove('has-error'));
    }
    function applyFieldErrors(fields) {
        clearEditorErrors();
        if (!fields) return;
        Object.keys(fields).forEach(name => {
            const el = document.querySelector(`.ag-error[data-error-for="${name}"]`);
            if (el) {
                el.textContent = fields[name];
                el.closest('.ag-field')?.classList.add('has-error');
            }
        });
    }

    async function saveEditor(e) {
        e.preventDefault();
        const id = $('editId').value;
        const dto = {
            name: $('editName').value.trim(),
            description: $('editDescription').value.trim() || null,
            instructions: $('editInstructions').value || null,
            isActive: $('editIsActive').checked,
            makeDefault: $('editMakeDefault').checked
        };
        const url = id ? `/dashboard/agents/${id}` : '/dashboard/agents';
        const r = await fetchJson(url, { method: 'POST', body: JSON.stringify(dto) });

        if (r.ok) {
            const detail = r.payload;
            const file = $('avatarFile').files?.[0];
            if (file && detail?.id) {
                const fd = new FormData();
                fd.append('avatar', file);
                const upResp = await fetch(`/dashboard/agents/${detail.id}/avatar`, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': csrf() },
                    body: fd
                });
                if (!upResp.ok) {
                    const err = await upResp.json().catch(() => ({}));
                    showToast(err.error || t('Agents.Toast.AvatarFailed'), 'error');
                }
            }
            closeEditor();
            showToast(id ? t('Agents.Toast.Updated') : t('Agents.Toast.Created'), 'success');
            load();
            return;
        }

        if (r.status === 422 && r.payload?.fields) {
            applyFieldErrors(r.payload.fields);
        } else if (r.status === 409) {
            showToast(r.payload?.error || t('Agents.Toast.SaveFailed'), 'error');
        } else {
            showToast(r.payload?.error || t('Agents.Toast.SaveFailed'), 'error');
        }
    }

    // ── Delete confirmation ──────────────────────────────────────────
    function openDeleteModal(a) {
        pendingDeleteId = a.id;
        $('deleteBody').textContent = t('Agents.Delete.Body', a.name);
        $('deleteWarning').classList.toggle('d-none', !a.isDefault);
        $('deleteConfirm').disabled = a.isDefault;
        $('deleteModal').classList.remove('d-none');
    }
    function closeDeleteModal() {
        $('deleteModal').classList.add('d-none');
        pendingDeleteId = null;
    }
    async function confirmDelete() {
        if (!pendingDeleteId) return;
        const r = await fetchJson(`/dashboard/agents/${pendingDeleteId}/delete`, { method: 'POST', body: '{}' });
        if (r.ok) {
            showToast(t('Agents.Toast.Deleted'), 'success');
            closeDeleteModal();
            load();
        } else {
            showToast(r.payload?.error || t('Agents.Toast.DeleteFailed'), 'error');
        }
    }

    // ── Inline SVG icons (consistent stroke width matches Lucide set) ──
    function svgWrap(size, body) {
        return `<svg class="icon" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;
    }
    function starIcon(size)    { return svgWrap(size, '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>'); }
    function pencilIcon(size)  { return svgWrap(size, '<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>'); }
    function userIcon(size)    { return svgWrap(size, '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>'); }
    function chatIcon(size)    { return svgWrap(size, '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>'); }
    function trashIcon(size)   { return svgWrap(size, '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>'); }

    // ── helpers ──────────────────────────────────────────────────────
    async function fetchJson(url, opts) {
        opts = opts || {};
        opts.headers = Object.assign({
            'Content-Type': 'application/json',
            'RequestVerificationToken': csrf()
        }, opts.headers || {});
        const resp = await fetch(url, opts);
        let payload = null;
        try { payload = await resp.json(); } catch {}
        return { ok: resp.ok, status: resp.status, payload };
    }

    // ── wiring ──────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('.agents-tab').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.agents-tab').forEach(b => {
                    b.classList.toggle('is-active', b === btn);
                    b.setAttribute('aria-selected', b === btn ? 'true' : 'false');
                });
                activeTab = btn.dataset.tab;
                load();
            });
        });

        $('createBtn')?.addEventListener('click', () => openEditor(null));
        $('editClose')?.addEventListener('click', closeEditor);
        $('editCancel')?.addEventListener('click', closeEditor);
        $('editForm')?.addEventListener('submit', saveEditor);

        $('deleteClose')?.addEventListener('click', closeDeleteModal);
        $('deleteCancel')?.addEventListener('click', closeDeleteModal);
        $('deleteConfirm')?.addEventListener('click', confirmDelete);

        $('avatarFile')?.addEventListener('change', e => {
            const f = e.target.files?.[0];
            if (!f) return;
            const url = URL.createObjectURL(f);
            renderAvatarPreview(url);
        });

        // Instructions live char counter.
        $('editInstructions')?.addEventListener('input', updateInstructionsCount);

        // Interaction guard: while "default" is checked, the agent must stay active.
        // Unchecking "default" releases that lock so the user can deactivate.
        $('editMakeDefault')?.addEventListener('change', e => {
            if (e.target.checked) {
                $('editIsActive').checked = true;
                $('editIsActive').disabled = true;
            } else {
                $('editIsActive').disabled = false;
            }
        });

        let searchTimer = null;
        $('searchInput')?.addEventListener('input', () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(load, 220);
        });

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') {
                if (!$('editModal')?.classList.contains('d-none')) closeEditor();
                if (!$('deleteModal')?.classList.contains('d-none')) closeDeleteModal();
            }
        });

        load();
    });
})();
