// Billing analytics page — KPIs, hand-rolled SVG bar chart, paginated transactions
// log with filters, CSV export. Self-contained, no chart library.
(function () {
    'use strict';

    let currentRange = 'mtd';
    let logState = { page: 0, pageSize: 50, kind: '', status: '' };

    function $(id) { return document.getElementById(id); }
    function fmtUsd(n) {
        return '$' + Number(n).toLocaleString(window.__i18nCulture__ || 'en', {
            minimumFractionDigits: 2, maximumFractionDigits: 2
        });
    }
    function fmtDate(iso, opts) {
        return i18n.formatDate(iso, opts || { month: 'short', day: 'numeric' });
    }
    function fmtTime(iso) {
        return i18n.formatDate(iso, { month: 'short', day: 'numeric' }) + ' ' +
               i18n.formatTime(iso, { hour: '2-digit', minute: '2-digit' });
    }
    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    // ── KPIs ────────────────────────────────────────────────────────────
    function renderKpis(data) {
        const tiles = [
            { key: 'TotalSpent',   label: t('Billing.Analytics.Kpi.Spent'),    value: fmtUsd(data.totalSpentUsd),     sub: t('Billing.Analytics.Kpi.SpentSub', data.paidMessageCount) },
            { key: 'TotalTopUp',   label: t('Billing.Analytics.Kpi.TopUp'),    value: fmtUsd(data.totalToppedUpUsd),  sub: '' },
            { key: 'MetaCost',     label: t('Billing.Analytics.Kpi.MetaCost'), value: fmtUsd(data.totalMetaCostUsd),  sub: t('Billing.Analytics.Kpi.MetaCostSub') },
            { key: 'Margin',       label: t('Billing.Analytics.Kpi.Margin'),   value: fmtUsd(data.marginUsd),         sub: t('Billing.Analytics.Kpi.MarginSub'), tone: 'is-positive' },
            { key: 'Free',         label: t('Billing.Analytics.Kpi.Free'),     value: data.freeMessageCount,          sub: t('Billing.Analytics.Kpi.FreeSub') }
        ];
        $('kpis').innerHTML = tiles.map(k => `
            <div class="an-kpi ${k.tone || ''}">
                <div class="an-kpi-label">${escapeHtml(k.label)}</div>
                <div class="an-kpi-value">${escapeHtml(String(k.value))}</div>
                ${k.sub ? `<div class="an-kpi-sub">${escapeHtml(k.sub)}</div>` : ''}
            </div>
        `).join('');
    }

    // ── Bar chart ───────────────────────────────────────────────────────
    function renderChart(daily) {
        const wrap = $('chart');
        wrap.innerHTML = '';
        if (!daily || !daily.length) {
            wrap.innerHTML = `<div style="color: var(--text-muted); padding: 20px; text-align: center;">${escapeHtml(t('Billing.Analytics.Chart.Empty'))}</div>`;
            return;
        }

        const w = Math.max(wrap.clientWidth, daily.length * 48);
        const h = 240;
        const padL = 48, padR = 12, padT = 12, padB = 32;
        const innerW = w - padL - padR;
        const innerH = h - padT - padB;

        const max = Math.max(1, ...daily.map(d => Math.max(d.spentUsd, d.toppedUpUsd)));
        const niceMax = niceUpper(max);

        const barGroupW = innerW / daily.length;
        const barW = Math.max(4, (barGroupW - 6) / 2);

        const svgNS = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(svgNS, 'svg');
        svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
        svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');

        // Grid lines + Y labels.
        for (let i = 0; i <= 4; i++) {
            const y = padT + (innerH * i / 4);
            const value = niceMax * (1 - i / 4);
            const gl = document.createElementNS(svgNS, 'line');
            gl.setAttribute('x1', padL); gl.setAttribute('y1', y);
            gl.setAttribute('x2', w - padR); gl.setAttribute('y2', y);
            gl.setAttribute('class', i === 4 ? 'an-axis-line' : 'an-grid-line');
            svg.appendChild(gl);
            const tx = document.createElementNS(svgNS, 'text');
            tx.setAttribute('x', padL - 6); tx.setAttribute('y', y + 3);
            tx.setAttribute('text-anchor', 'end');
            tx.setAttribute('class', 'an-axis-label');
            tx.textContent = '$' + Math.round(value);
            svg.appendChild(tx);
        }

        // Bars + day labels (skip every other label when dense).
        const labelStep = daily.length > 14 ? Math.ceil(daily.length / 14) : 1;
        daily.forEach((d, i) => {
            const x = padL + i * barGroupW + 3;
            const spentH = (d.spentUsd / niceMax) * innerH;
            const topupH = (d.toppedUpUsd / niceMax) * innerH;

            if (spentH > 0) {
                const r1 = document.createElementNS(svgNS, 'rect');
                r1.setAttribute('class', 'an-bar-spent');
                r1.setAttribute('x', x);
                r1.setAttribute('y', padT + (innerH - spentH));
                r1.setAttribute('width', barW);
                r1.setAttribute('height', spentH);
                r1.setAttribute('rx', 2);
                const title = document.createElementNS(svgNS, 'title');
                title.textContent = `${fmtDate(d.dayUtc)} — ${t('Billing.Analytics.Chart.Spent')}: ${fmtUsd(d.spentUsd)}`;
                r1.appendChild(title);
                svg.appendChild(r1);
            }
            if (topupH > 0) {
                const r2 = document.createElementNS(svgNS, 'rect');
                r2.setAttribute('class', 'an-bar-topup');
                r2.setAttribute('x', x + barW + 2);
                r2.setAttribute('y', padT + (innerH - topupH));
                r2.setAttribute('width', barW);
                r2.setAttribute('height', topupH);
                r2.setAttribute('rx', 2);
                const title = document.createElementNS(svgNS, 'title');
                title.textContent = `${fmtDate(d.dayUtc)} — ${t('Billing.Analytics.Chart.TopUp')}: ${fmtUsd(d.toppedUpUsd)}`;
                r2.appendChild(title);
                svg.appendChild(r2);
            }

            if (i % labelStep === 0) {
                const label = document.createElementNS(svgNS, 'text');
                label.setAttribute('x', x + barW);
                label.setAttribute('y', h - padB + 14);
                label.setAttribute('text-anchor', 'middle');
                label.setAttribute('class', 'an-axis-label');
                label.textContent = fmtDate(d.dayUtc, { month: 'numeric', day: 'numeric' });
                svg.appendChild(label);
            }
        });

        wrap.appendChild(svg);
    }

    function niceUpper(v) {
        if (v <= 0) return 1;
        const exp = Math.pow(10, Math.floor(Math.log10(v)));
        const norm = v / exp;
        const nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
        return nice * exp;
    }

    // ── Categories ──────────────────────────────────────────────────────
    function renderCategories(byCat) {
        const tbody = document.querySelector('#catTable tbody');
        if (!byCat.length) {
            tbody.innerHTML = `<tr><td colspan="3" style="text-align:center; color: var(--text-muted); padding: 16px;">${escapeHtml(t('Billing.Analytics.Categories.Empty'))}</td></tr>`;
            return;
        }
        tbody.innerHTML = byCat.map(c => `
            <tr>
                <td>${escapeHtml(catLabel(c.category))}</td>
                <td>${c.count}</td>
                <td>${escapeHtml(fmtUsd(c.chargedUsd))}</td>
            </tr>
        `).join('');
    }
    function catLabel(c) {
        return c === 0 ? t('Templates.Category.Service') || 'Service'
             : c === 1 ? t('Templates.Category.Utility')
             : c === 2 ? t('Templates.Category.Authentication')
             : c === 3 ? t('Templates.Category.Marketing')
             : '—';
    }

    // ── Transactions log ────────────────────────────────────────────────
    async function loadAnalytics() {
        const resp = await fetch('/dashboard/billing/analytics/data?range=' + encodeURIComponent(currentRange));
        if (!resp.ok) return;
        const data = await resp.json();
        renderKpis(data);
        renderChart(data.daily);
        renderCategories(data.byCategory);
    }

    async function loadLog() {
        const params = new URLSearchParams({
            range: currentRange,
            page: String(logState.page),
            pageSize: String(logState.pageSize)
        });
        if (logState.kind !== '') params.set('kind', logState.kind);
        if (logState.status !== '') params.set('status', logState.status);

        const resp = await fetch('/dashboard/billing/transactions?' + params.toString());
        if (!resp.ok) return;
        const page = await resp.json();
        renderLog(page);
    }

    function renderLog(page) {
        const tbody = document.querySelector('#logTable tbody');
        if (!page.rows.length) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color: var(--text-muted); padding: 16px;">${escapeHtml(t('Billing.Analytics.Log.Empty'))}</td></tr>`;
        } else {
            tbody.innerHTML = page.rows.map(r => {
                const amtCls = r.amountUsd >= 0 ? 'an-amount-positive' : 'an-amount-negative';
                const amtStr = (r.amountUsd >= 0 ? '+' : '') + fmtUsd(r.amountUsd);
                return `
                    <tr>
                        <td>${escapeHtml(fmtTime(r.createdAt))}</td>
                        <td>${escapeHtml(kindLabel(r.kind))}</td>
                        <td class="${amtCls}">${escapeHtml(amtStr)}</td>
                        <td>${escapeHtml(fmtUsd(r.balanceAfterUsd))}</td>
                        <td>${escapeHtml(statusLabel(r.status))}</td>
                        <td>${escapeHtml(r.reference || r.reason || '')}</td>
                    </tr>
                `;
            }).join('');
        }

        const totalPages = Math.max(1, Math.ceil(page.totalCount / page.pageSize));
        $('logPageInfo').textContent = t('Billing.Analytics.Log.PageInfo', page.page + 1, totalPages, page.totalCount);
        $('logPrev').disabled = page.page <= 0;
        $('logNext').disabled = !page.hasMore;
    }

    function kindLabel(k) {
        return k === 0 ? t('Billing.Kind.TopUp')
             : k === 1 ? t('Billing.Kind.MessageCharge')
             : k === 2 ? t('Billing.Kind.MessageRefund')
             : k === 3 ? t('Billing.Kind.ManualCredit')
             : k === 4 ? t('Billing.Kind.ManualDebit')
             : k === 5 ? t('Billing.Kind.AutoRecharge')
             : '—';
    }
    function statusLabel(s) {
        return s === 0 ? t('Billing.Analytics.Status.Pending')
             : s === 1 ? t('Billing.Analytics.Status.Succeeded')
             : s === 2 ? t('Billing.Analytics.Status.Failed')
             : s === 3 ? t('Billing.Analytics.Status.Refunded')
             : '—';
    }

    function buildCsvHref() {
        const params = new URLSearchParams({ range: currentRange });
        if (logState.kind !== '') params.set('kind', logState.kind);
        if (logState.status !== '') params.set('status', logState.status);
        return '/dashboard/billing/transactions.csv?' + params.toString();
    }

    document.addEventListener('DOMContentLoaded', () => {
        $('rangeSel').addEventListener('change', e => {
            currentRange = e.target.value;
            logState.page = 0;
            $('csvExport').setAttribute('href', buildCsvHref());
            loadAnalytics();
            loadLog();
        });
        $('kindFilter').addEventListener('change', e => {
            logState.kind = e.target.value;
            logState.page = 0;
            $('csvExport').setAttribute('href', buildCsvHref());
            loadLog();
        });
        $('statusFilter').addEventListener('change', e => {
            logState.status = e.target.value;
            logState.page = 0;
            loadLog();
        });
        $('logPrev').addEventListener('click', () => {
            if (logState.page > 0) { logState.page--; loadLog(); }
        });
        $('logNext').addEventListener('click', () => {
            logState.page++; loadLog();
        });
        $('csvExport').setAttribute('href', buildCsvHref());

        loadAnalytics();
        loadLog();
    });
})();
