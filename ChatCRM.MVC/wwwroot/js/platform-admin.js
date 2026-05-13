// Platform admin overview page — KPI tiles, top spenders, category breakdown.
(function () {
    'use strict';

    let currentRange = 'mtd';

    function $(id) { return document.getElementById(id); }
    function fmtUsd(n) { return '$' + Number(n).toLocaleString(window.__i18nCulture__ || 'en', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }
    function catLabel(c) {
        return c === 0 ? t('Templates.Category.Service')
             : c === 1 ? t('Templates.Category.Utility')
             : c === 2 ? t('Templates.Category.Authentication')
             : c === 3 ? t('Templates.Category.Marketing')
             : '—';
    }

    function renderKpis(d) {
        const tiles = [
            { label: t('PlatformAdmin.Kpi.Revenue'),     value: fmtUsd(d.totalRevenueUsd) },
            { label: t('PlatformAdmin.Kpi.MetaCost'),    value: fmtUsd(d.totalMetaCostUsd) },
            { label: t('PlatformAdmin.Kpi.Margin'),      value: fmtUsd(d.marginUsd), tone: 'is-positive', sub: d.marginPercent + '%' },
            { label: t('PlatformAdmin.Kpi.PaidMessages'),value: d.totalPaidMessageCount },
            { label: t('PlatformAdmin.Kpi.Workspaces'),  value: d.activeWorkspaceCount }
        ];
        $('kpis').innerHTML = tiles.map(k => `
            <div class="an-kpi ${k.tone || ''}">
                <div class="an-kpi-label">${escapeHtml(k.label)}</div>
                <div class="an-kpi-value">${escapeHtml(String(k.value))}</div>
                ${k.sub ? `<div class="an-kpi-sub">${escapeHtml(k.sub)}</div>` : ''}
            </div>
        `).join('');
    }

    function renderTop(rows) {
        const tbody = document.querySelector('#topTable tbody');
        if (!rows.length) {
            tbody.innerHTML = `<tr><td colspan="4" style="text-align:center; color: var(--text-muted); padding: 16px;">${escapeHtml(t('PlatformAdmin.Empty'))}</td></tr>`;
            return;
        }
        tbody.innerHTML = rows.map(r => `
            <tr>
                <td>#${r.workspaceId}</td>
                <td>${escapeHtml(fmtUsd(r.spentUsd))}</td>
                <td>${r.messageCount}</td>
                <td>${escapeHtml(fmtUsd(r.currentBalanceUsd))}</td>
            </tr>
        `).join('');
    }

    function renderCategories(rows) {
        const tbody = document.querySelector('#catTable tbody');
        if (!rows.length) {
            tbody.innerHTML = `<tr><td colspan="3" style="text-align:center; color: var(--text-muted); padding: 16px;">${escapeHtml(t('Billing.Analytics.Categories.Empty'))}</td></tr>`;
            return;
        }
        tbody.innerHTML = rows.map(c => `
            <tr>
                <td>${escapeHtml(catLabel(c.category))}</td>
                <td>${c.count}</td>
                <td>${escapeHtml(fmtUsd(c.chargedUsd))}</td>
            </tr>
        `).join('');
    }

    async function load() {
        const resp = await fetch('/dashboard/platform-admin/overview?range=' + encodeURIComponent(currentRange));
        if (!resp.ok) return;
        const d = await resp.json();
        renderKpis(d);
        renderTop(d.topSpenders || []);
        renderCategories(d.byCategory || []);
    }

    document.addEventListener('DOMContentLoaded', () => {
        $('rangeSel').addEventListener('change', e => { currentRange = e.target.value; load(); });
        load();
    });
})();
