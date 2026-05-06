// Audit log — paged, filtered table view of BillingAuditLog rows.
(function () {
    'use strict';

    let state = { page: 0, pageSize: 50, action: '', entityType: '', actor: '', entityId: '' };

    function $(id) { return document.getElementById(id); }
    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }
    function fmtTime(iso) {
        const d = new Date(iso);
        return d.toLocaleDateString(window.__i18nCulture__ || 'en', { year: 'numeric', month: 'short', day: 'numeric' }) + ' ' +
               d.toLocaleTimeString(window.__i18nCulture__ || 'en', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    }

    async function loadFilters() {
        const resp = await fetch('/dashboard/audit/filters');
        if (!resp.ok) return;
        const { actions, entityTypes } = await resp.json();
        const actionSel = $('filterAction');
        const typeSel = $('filterEntityType');
        actions.forEach(a => {
            const opt = document.createElement('option');
            opt.value = a; opt.textContent = a;
            actionSel.appendChild(opt);
        });
        entityTypes.forEach(et => {
            const opt = document.createElement('option');
            opt.value = et; opt.textContent = et;
            typeSel.appendChild(opt);
        });
    }

    async function loadPage() {
        const params = new URLSearchParams({ Page: String(state.page), PageSize: String(state.pageSize) });
        if (state.action) params.set('Action', state.action);
        if (state.entityType) params.set('EntityType', state.entityType);
        if (state.actor) params.set('Actor', state.actor);
        if (state.entityId) params.set('EntityId', state.entityId);

        const resp = await fetch('/dashboard/audit/query?' + params.toString());
        if (!resp.ok) return;
        const data = await resp.json();
        renderTable(data);
    }

    function renderTable(page) {
        const tbody = document.querySelector('#auditTable tbody');
        if (!page.rows.length) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color: var(--text-muted); padding: 16px;">${escapeHtml(t('Audit.Empty'))}</td></tr>`;
        } else {
            tbody.innerHTML = page.rows.map(r => `
                <tr class="audit-row" data-id="${r.id}">
                    <td><button type="button" class="audit-row-toggle" data-toggle="${r.id}">▸</button></td>
                    <td>${escapeHtml(fmtTime(r.atUtc))}</td>
                    <td>${escapeHtml(r.actor)}</td>
                    <td><code style="font-size: 11.5px;">${escapeHtml(r.action)}</code></td>
                    <td>${escapeHtml(r.entityType)}</td>
                    <td>${escapeHtml(r.entityId)}</td>
                </tr>
            `).join('');

            tbody.querySelectorAll('[data-toggle]').forEach(btn => {
                btn.addEventListener('click', () => toggleDetail(Number(btn.dataset.toggle), page.rows.find(r => r.id === Number(btn.dataset.toggle))));
            });
        }

        const totalPages = Math.max(1, Math.ceil(page.totalCount / page.pageSize));
        $('pageInfo').textContent = t('Billing.Analytics.Log.PageInfo', page.page + 1, totalPages, page.totalCount);
        $('prevBtn').disabled = page.page <= 0;
        $('nextBtn').disabled = !page.hasMore;
    }

    function toggleDetail(id, row) {
        if (!row) return;
        const tr = document.querySelector(`tr.audit-row[data-id="${id}"]`);
        if (!tr) return;
        const next = tr.nextElementSibling;
        if (next?.classList.contains('audit-row-detail-row')) {
            next.remove();
            tr.classList.remove('is-open');
            return;
        }
        const detail = document.createElement('tr');
        detail.className = 'audit-row-detail-row';
        const before = row.beforeJson ? `<div><strong>${escapeHtml(t('Audit.Detail.Before'))}:</strong></div><div class="audit-row-detail">${escapeHtml(row.beforeJson)}</div>` : '';
        const after  = row.afterJson  ? `<div style="margin-top: 8px;"><strong>${escapeHtml(t('Audit.Detail.After'))}:</strong></div><div class="audit-row-detail">${escapeHtml(row.afterJson)}</div>` : '';
        detail.innerHTML = `<td colspan="6">${before}${after || (!before ? `<em style="color: var(--text-muted);">${escapeHtml(t('Audit.Detail.None'))}</em>` : '')}</td>`;
        tr.classList.add('is-open');
        tr.parentElement.insertBefore(detail, tr.nextSibling);
    }

    document.addEventListener('DOMContentLoaded', async () => {
        await loadFilters();

        $('applyFilters').addEventListener('click', () => {
            state.action = $('filterAction').value;
            state.entityType = $('filterEntityType').value;
            state.actor = $('filterActor').value.trim();
            state.entityId = $('filterEntityId').value.trim();
            state.page = 0;
            loadPage();
        });
        $('resetFilters').addEventListener('click', () => {
            $('filterAction').value = '';
            $('filterEntityType').value = '';
            $('filterActor').value = '';
            $('filterEntityId').value = '';
            state = { page: 0, pageSize: 50, action: '', entityType: '', actor: '', entityId: '' };
            loadPage();
        });
        $('prevBtn').addEventListener('click', () => { if (state.page > 0) { state.page--; loadPage(); } });
        $('nextBtn').addEventListener('click', () => { state.page++; loadPage(); });

        loadPage();
    });
})();
