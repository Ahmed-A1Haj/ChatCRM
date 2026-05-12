// Invoices list — generate / issue / download PDF.
(function () {
    'use strict';

    const caps = window.__caps__ || { canIssue: false };

    function $(id) { return document.getElementById(id); }
    function csrf() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''; }
    function fmtUsd(n) { return '$' + Number(n).toLocaleString(window.__i18nCulture__ || 'en', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function fmtMonth(iso) {
        const d = new Date(iso);
        return d.toLocaleDateString(window.__i18nCulture__ || 'en', { year: 'numeric', month: 'long' });
    }
    function fmtDate(iso) {
        if (!iso) return '—';
        const d = new Date(iso);
        return d.toLocaleDateString(window.__i18nCulture__ || 'en', { year: 'numeric', month: 'short', day: 'numeric' });
    }
    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }
    function showToast(msg, kind) {
        const el = $('toast');
        if (!el) return;
        el.textContent = msg;
        el.classList.remove('is-error', 'is-success', 'd-none');
        if (kind === 'error') el.classList.add('is-error');
        else if (kind === 'success') el.classList.add('is-success');
        clearTimeout(el._hide);
        el._hide = setTimeout(() => el.classList.add('d-none'), 4500);
    }
    function statusKey(s) { return s === 1 ? 'Issued' : s === 2 ? 'Void' : 'Draft'; }

    function populateMonthDropdown() {
        const sel = $('genMonth');
        if (!sel) return;
        const now = new Date();
        for (let i = 0; i < 12; i++) {
            const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
            const opt = document.createElement('option');
            opt.value = `${d.getFullYear()}-${d.getMonth() + 1}`;
            opt.textContent = d.toLocaleDateString(window.__i18nCulture__ || 'en', { year: 'numeric', month: 'long' });
            sel.appendChild(opt);
        }
    }

    async function loadList() {
        const resp = await fetch('/dashboard/billing/invoices/list');
        if (!resp.ok) return;
        const rows = await resp.json();
        const tbody = document.querySelector('#invoiceTable tbody');
        const empty = $('emptyRow');
        if (!rows.length) {
            tbody.innerHTML = '';
            empty.style.display = 'flex';
            return;
        }
        empty.style.display = 'none';
        tbody.innerHTML = rows.map(r => {
            const issuedActions = r.status === 0
                ? (caps.canIssue ? `<button type="button" class="tpl-row-btn" data-act="issue" data-id="${r.id}">${escapeHtml(t('Invoices.Action.Issue'))}</button>` : '')
                : '';
            return `
                <tr>
                    <td><strong>${escapeHtml(r.number)}</strong></td>
                    <td>${escapeHtml(fmtMonth(r.periodStartUtc))}</td>
                    <td><span class="tpl-status s-${statusKey(r.status).toLowerCase()}">${escapeHtml(t('Invoices.Status.' + statusKey(r.status)))}</span></td>
                    <td>${escapeHtml(fmtUsd(r.totalChargedUsd))}</td>
                    <td>${escapeHtml(fmtDate(r.issuedAt))}${r.issuedByName ? ` <span style="color: var(--text-muted); font-size: 11.5px;">${escapeHtml(r.issuedByName)}</span>` : ''}</td>
                    <td class="tpl-col-actions">
                        <a class="tpl-row-btn" href="/dashboard/billing/invoices/${r.id}.pdf" target="_blank" rel="noopener">${escapeHtml(t('Invoices.Action.Pdf'))}</a>
                        ${issuedActions}
                    </td>
                </tr>
            `;
        }).join('');

        document.querySelectorAll('[data-act="issue"]').forEach(btn => {
            btn.addEventListener('click', async () => {
                if (!confirm(t('Invoices.Confirm.Issue'))) return;
                btn.disabled = true;
                try {
                    const resp = await fetch(`/dashboard/billing/invoices/${btn.dataset.id}/issue`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf() },
                        body: JSON.stringify({})
                    });
                    if (resp.ok) {
                        await loadList();
                        showToast(t('Invoices.Toast.Issued'), 'success');
                    } else {
                        const err = await resp.json().catch(() => ({}));
                        showToast(err.error || t('Invoices.Toast.IssueFailed'), 'error');
                    }
                } finally {
                    btn.disabled = false;
                }
            });
        });
    }

    async function generate() {
        const sel = $('genMonth');
        if (!sel) return;
        const [year, month] = sel.value.split('-').map(Number);
        $('genBtn').disabled = true;
        try {
            const resp = await fetch('/dashboard/billing/invoices/generate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': csrf() },
                body: JSON.stringify({ year, month })
            });
            if (resp.ok) {
                await loadList();
                showToast(t('Invoices.Toast.Generated'), 'success');
            } else {
                const err = await resp.json().catch(() => ({}));
                showToast(err.error || t('Invoices.Toast.GenerateFailed'), 'error');
            }
        } finally {
            $('genBtn').disabled = false;
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        populateMonthDropdown();
        $('genBtn')?.addEventListener('click', generate);
        loadList();
    });
})();
