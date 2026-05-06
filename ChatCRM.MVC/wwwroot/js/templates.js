// Templates manager page — list/create/edit/submit/delete + sync, all via fetch().
// Self-contained: no framework. Mirrors the patterns used in users.js / contacts.js.
(function () {
    'use strict';

    const STATUS = {
        0: 'draft', 1: 'submitted', 2: 'approved', 3: 'rejected',
        4: 'paused', 5: 'disabled', 6: 'stuck'
    };
    const CATEGORY_KEY = {
        1: 'Templates.Category.Utility',
        2: 'Templates.Category.Authentication',
        3: 'Templates.Category.Marketing'
    };

    const caps = window.__caps__ || { canCreate: false, canSubmit: false, canDelete: false };

    let activeTab = 'active';
    let cachedRows = { active: null, archived: null };
    let editorMode = 'create'; // 'create' | 'edit'

    // ── helpers ─────────────────────────────────────────────────────────
    function $(id) { return document.getElementById(id); }
    function csrf() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''; }

    function showToast(message, kind) {
        const el = $('toast');
        if (!el) return;
        el.textContent = message;
        el.classList.remove('is-error', 'is-success', 'd-none');
        if (kind === 'error') el.classList.add('is-error');
        else if (kind === 'success') el.classList.add('is-success');
        clearTimeout(el._hide);
        el._hide = setTimeout(() => el.classList.add('d-none'), 4500);
    }

    async function fetchJson(url, options) {
        const resp = await fetch(url, options);
        let payload = null;
        try { payload = await resp.json(); } catch { /* not JSON */ }
        return { ok: resp.ok, status: resp.status, payload };
    }

    function statusLabel(status) {
        return t('Templates.Status.' + (STATUS[status] || 'submitted'));
    }
    function categoryLabel(cat) {
        return t(CATEGORY_KEY[cat] || 'Templates.Category.Utility');
    }
    function fmtDate(iso) {
        if (!iso) return '—';
        try { return i18n.formatDate(iso, { year: 'numeric', month: 'short', day: 'numeric' }); }
        catch { return iso; }
    }

    // ── preflight ───────────────────────────────────────────────────────
    async function loadState() {
        const { ok, payload } = await fetchJson('/dashboard/templates/state');
        if (!ok || !payload) return;

        const banner = $('preflightBanner');
        banner.classList.add('d-none', 'auth-alert-info', 'auth-alert-warning', 'auth-alert-danger');

        if (!payload.hasBusinessInstance) {
            banner.textContent = t('Templates.Preflight.NoBusinessInstance');
            banner.classList.remove('d-none');
            banner.classList.add('auth-alert-warning');
        } else if (!payload.isConfigured) {
            banner.textContent = t('Templates.Preflight.NotConfigured');
            banner.classList.remove('d-none');
            banner.classList.add('auth-alert-warning');
        } else if (payload.hasAuthFailure) {
            banner.textContent = t('Templates.Preflight.AuthFailure');
            banner.classList.remove('d-none');
            banner.classList.add('auth-alert-danger');
        }

        const rateBanner = $('rateBanner');
        if (payload.submissionsLastHour >= payload.submissionsLimitPerHour) {
            rateBanner.textContent = t('Templates.Preflight.RateLimitedNow');
            rateBanner.classList.remove('d-none');
        } else if (payload.submissionsLastHour >= Math.ceil(payload.submissionsLimitPerHour * 0.7)) {
            rateBanner.textContent = t('Templates.Preflight.RateLimitWarning',
                payload.submissionsLastHour, payload.submissionsLimitPerHour);
            rateBanner.classList.remove('d-none');
        } else {
            rateBanner.classList.add('d-none');
        }
    }

    // ── list rendering ──────────────────────────────────────────────────
    async function loadList(force) {
        const tab = activeTab;
        if (!force && cachedRows[tab] != null) {
            renderTable(cachedRows[tab]);
            return;
        }
        const archived = (tab === 'archived');
        const { ok, payload } = await fetchJson('/dashboard/templates/list?archived=' + archived);
        if (!ok || !Array.isArray(payload)) {
            showToast(t('Templates.Toast.LoadFailed'), 'error');
            return;
        }
        cachedRows[tab] = payload;
        renderTable(payload);
        $('count' + (tab === 'active' ? 'Active' : 'Archived')).textContent = payload.length;
    }

    function renderTable(rows) {
        const body = $('tplBody');
        const empty = $('emptyState');
        const table = $('tplTable');

        if (!rows.length) {
            table.classList.add('d-none');
            empty.classList.remove('d-none');
            $('emptyTitle').textContent = activeTab === 'archived'
                ? t('Templates.Empty.ArchivedTitle')
                : t('Templates.Empty.Title');
            $('emptySub').textContent = activeTab === 'archived'
                ? t('Templates.Empty.ArchivedSub')
                : t('Templates.Empty.Sub');
            return;
        }

        empty.classList.add('d-none');
        table.classList.remove('d-none');
        body.innerHTML = '';

        rows.forEach(row => {
            const tr = document.createElement('tr');
            tr.dataset.id = row.id;

            const statusKey = STATUS[row.status] || 'submitted';
            const rejectionLine = row.rejectionReason
                ? `<span class="tpl-name-sub" title="${escapeAttr(row.rejectionReason)}">${escapeHtml(row.rejectionReason)}</span>`
                : '';

            tr.innerHTML = `
                <td>
                    <span class="tpl-name">${escapeHtml(row.name)}</span>
                    ${rejectionLine}
                </td>
                <td>${categoryLabel(row.category)}</td>
                <td>${escapeHtml(row.languageCode)}</td>
                <td><span class="tpl-status s-${statusKey}">${statusLabel(row.status)}</span></td>
                <td>${fmtDate(row.updatedAt)}</td>
                <td class="tpl-col-actions">${renderActions(row)}</td>
            `;
            body.appendChild(tr);
        });

        body.querySelectorAll('[data-act]').forEach(btn => {
            btn.addEventListener('click', () => handleRowAction(
                btn.dataset.act,
                Number(btn.closest('tr').dataset.id)
            ));
        });
    }

    function renderActions(row) {
        const parts = [];
        const isDraftOrRejected = row.status === 0 || row.status === 3;
        const isActive = row.status !== 5;

        if (caps.canCreate && isDraftOrRejected) {
            parts.push(`<button type="button" class="tpl-row-btn" data-act="edit">${t('Templates.Action.Edit')}</button>`);
        }
        if (caps.canSubmit && isDraftOrRejected) {
            parts.push(`<button type="button" class="tpl-row-btn" data-act="submit">${t('Templates.Action.Submit')}</button>`);
        }
        if (caps.canDelete && isActive) {
            parts.push(`<button type="button" class="tpl-row-btn tpl-row-btn-danger" data-act="delete">${t('Templates.Action.Archive')}</button>`);
        }
        return parts.join('') || `<span class="tpl-name-sub">${t('Templates.Action.None')}</span>`;
    }

    async function handleRowAction(action, id) {
        if (action === 'edit') return openEditor(id);
        if (action === 'submit') return submitTemplate(id);
        if (action === 'delete') return archiveTemplate(id);
    }

    // ── editor (create / edit) ──────────────────────────────────────────
    async function openEditor(id) {
        editorMode = id ? 'edit' : 'create';
        clearEditorErrors();
        $('editId').value = id || '';

        if (id) {
            const { ok, payload } = await fetchJson('/dashboard/templates/' + id);
            if (!ok || !payload) {
                showToast(t('Templates.Toast.LoadFailed'), 'error');
                return;
            }
            $('editName').value = payload.name;
            $('editCategory').value = String(payload.category);
            $('editLanguage').value = payload.languageCode;
            $('editBody').value = payload.body;
            $('editFooter').value = payload.footer || '';
        } else {
            $('editName').value = '';
            $('editCategory').value = '1'; // Utility default
            $('editLanguage').value = 'en_US';
            $('editBody').value = '';
            $('editFooter').value = '';
        }

        $('editTitle').textContent = id
            ? t('Templates.Editor.EditTitle')
            : t('Templates.Editor.Title');
        $('editModal').classList.remove('d-none');
        $('editName').focus();
    }

    function closeEditor() {
        $('editModal').classList.add('d-none');
    }

    function clearEditorErrors() {
        document.querySelectorAll('.tpl-error[data-error-for]').forEach(e => { e.textContent = ''; });
        document.querySelectorAll('.tpl-field.has-error').forEach(f => f.classList.remove('has-error'));
    }

    function applyFieldErrors(fields) {
        clearEditorErrors();
        if (!fields) return;
        Object.keys(fields).forEach(name => {
            const el = document.querySelector(`.tpl-error[data-error-for="${name}"]`);
            if (el) {
                el.textContent = fields[name];
                const wrap = el.closest('.tpl-field');
                if (wrap) wrap.classList.add('has-error');
            }
        });
    }

    function readEditor() {
        return {
            name: $('editName').value.trim(),
            category: Number($('editCategory').value),
            languageCode: $('editLanguage').value,
            body: $('editBody').value,
            footer: $('editFooter').value.trim() || null
        };
    }

    async function saveDraft() {
        const dto = readEditor();
        const id = $('editId').value;
        const url = id ? `/dashboard/templates/${id}/draft` : '/dashboard/templates/draft';
        const { ok, status, payload } = await fetchJson(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': csrf()
            },
            body: JSON.stringify(dto)
        });

        if (ok) {
            closeEditor();
            cachedRows.active = null;
            await loadList(true);
            showToast(t('Templates.Toast.Saved'), 'success');
            return payload?.id;
        }

        if (status === 422 && payload?.fields) {
            applyFieldErrors(payload.fields);
        } else {
            showToast(payload?.error || t('Templates.Toast.SaveFailed'), 'error');
        }
        return null;
    }

    async function saveAndSubmit() {
        const id = await saveDraft();
        if (id) await submitTemplate(id);
    }

    async function submitTemplate(id) {
        const { ok, status, payload } = await fetchJson(`/dashboard/templates/${id}/submit`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': csrf()
            },
            body: JSON.stringify({})
        });

        if (ok) {
            cachedRows.active = null;
            await Promise.all([loadList(true), loadState()]);
            showToast(t('Templates.Toast.Submitted'), 'success');
            return;
        }

        if (status === 429 && payload?.retryAfterMinutes) {
            showToast(t('Templates.Toast.RateLimited', payload.retryAfterMinutes), 'error');
        } else if (status === 401) {
            showToast(payload?.error || t('Templates.Error.AuthFailed'), 'error');
            await loadState();
        } else if (status === 409) {
            showToast(payload?.error || t('Templates.Error.NotConfigured'), 'error');
        } else if (status === 422 && payload?.fields) {
            // Validation failure on a row that wasn't open for edit — surface as toast.
            const first = Object.values(payload.fields)[0] || payload?.error;
            showToast(first || t('Templates.Toast.SaveFailed'), 'error');
        } else {
            showToast(payload?.error || t('Templates.Toast.SubmitFailed'), 'error');
        }
    }

    async function archiveTemplate(id) {
        if (!confirm(t('Templates.Confirm.Archive'))) return;
        const { ok, payload, status } = await fetchJson(`/dashboard/templates/${id}/delete`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': csrf()
            },
            body: JSON.stringify({})
        });
        if (ok) {
            cachedRows.active = null;
            cachedRows.archived = null;
            await loadList(true);
            showToast(t('Templates.Toast.Archived'), 'success');
        } else if (status === 401) {
            showToast(payload?.error || t('Templates.Error.AuthFailed'), 'error');
            await loadState();
        } else {
            showToast(payload?.error || t('Templates.Toast.ArchiveFailed'), 'error');
        }
    }

    async function syncFromMeta() {
        const btn = $('syncBtn');
        btn.disabled = true;
        try {
            const { ok, status, payload } = await fetchJson('/dashboard/templates/sync', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': csrf()
                },
                body: JSON.stringify({})
            });
            if (ok && payload) {
                cachedRows.active = null;
                cachedRows.archived = null;
                await loadList(true);
                showToast(t('Templates.Toast.SyncDone',
                    payload.remoteCount, payload.localUpdated, payload.localImported, payload.localOrphaned),
                    'success');
            } else if (status === 401) {
                showToast(payload?.error || t('Templates.Error.AuthFailed'), 'error');
                await loadState();
            } else if (status === 409) {
                showToast(payload?.error || t('Templates.Error.NotConfigured'), 'error');
            } else {
                showToast(payload?.error || t('Templates.Toast.SyncFailed'), 'error');
            }
        } finally {
            btn.disabled = false;
        }
    }

    // ── escaping (no innerHTML on user data without it) ─────────────────
    function escapeHtml(str) {
        return String(str ?? '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }
    function escapeAttr(str) { return escapeHtml(str); }

    // ── wiring ──────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', async () => {
        document.querySelectorAll('.tpl-tab').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('.tpl-tab').forEach(b => {
                    b.classList.toggle('is-active', b === btn);
                    b.setAttribute('aria-selected', b === btn ? 'true' : 'false');
                });
                activeTab = btn.dataset.tab;
                loadList(false);
            });
        });

        $('createBtn')?.addEventListener('click', () => openEditor(null));
        $('syncBtn')?.addEventListener('click', syncFromMeta);
        $('editClose')?.addEventListener('click', closeEditor);
        $('editCancel')?.addEventListener('click', closeEditor);

        $('editForm')?.addEventListener('submit', e => {
            e.preventDefault();
            saveDraft();
        });
        $('editSaveSubmit')?.addEventListener('click', saveAndSubmit);

        // Esc closes the modal
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && !$('editModal')?.classList.contains('d-none')) closeEditor();
        });

        // Pre-load both tab counts so the chip badges populate without a tab switch.
        await loadState();
        await Promise.all([
            (async () => { activeTab = 'active'; cachedRows.active = null; await loadList(true); })(),
            (async () => {
                const { ok, payload } = await fetchJson('/dashboard/templates/list?archived=true');
                if (ok && Array.isArray(payload)) {
                    cachedRows.archived = payload;
                    $('countArchived').textContent = payload.length;
                }
            })()
        ]);
    });
})();
