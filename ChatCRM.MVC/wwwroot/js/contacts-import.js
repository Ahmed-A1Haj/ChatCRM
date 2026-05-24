/* Contact import flow: download template, upload + preview, confirm import, error report.
   Built on top of the existing modal + toast plumbing in contacts.js (escapeHtml, t,
   showToast, token, refreshCurrentPage). */

(function () {
    'use strict';

    // ── DOM refs (resolved once on first open) ──────────────────────────
    let modal, stepChoose, stepPreview, stepResult, fileInput, dropzone, dropzoneHint;
    let previewBody, summaryEl, duplicatesSection, duplicatesHint;
    let errorsSection, errorsSummary, errorsList, downloadErrorsBtn;
    let resultContent, resultErrors, resultErrorsSummary, resultErrorsList, resultDownloadErrorsBtn;
    let backBtn, confirmBtn, confirmLabel, confirmSpinner;
    let cachedDom = false;

    let currentPreview = null;   // ImportPreviewDto returned by /preview
    let currentStep = 'choose';  // choose | preview | result

    function cacheDom() {
        if (cachedDom) return;
        modal = document.getElementById('importModal');
        stepChoose = document.getElementById('importStepChoose');
        stepPreview = document.getElementById('importStepPreview');
        stepResult = document.getElementById('importStepResult');
        fileInput = document.getElementById('importFileInput');
        dropzone = document.getElementById('importDropzone');
        dropzoneHint = document.getElementById('importDropzoneHint');
        previewBody = document.getElementById('importPreviewBody');
        summaryEl = document.getElementById('importSummary');
        duplicatesSection = document.getElementById('importDuplicatesSection');
        duplicatesHint = document.getElementById('importDuplicatesHint');
        errorsSection = document.getElementById('importErrorsSection');
        errorsSummary = document.getElementById('importErrorsSummary');
        errorsList = document.getElementById('importErrorsList');
        downloadErrorsBtn = document.getElementById('importDownloadErrorsBtn');
        resultContent = document.getElementById('importResultContent');
        resultErrors = document.getElementById('importResultErrors');
        resultErrorsSummary = document.getElementById('importResultErrorsSummary');
        resultErrorsList = document.getElementById('importResultErrorsList');
        resultDownloadErrorsBtn = document.getElementById('importResultDownloadErrorsBtn');
        backBtn = document.getElementById('importBackBtn');
        confirmBtn = document.getElementById('importConfirmBtn');
        confirmLabel = document.getElementById('importConfirmLabel');
        confirmSpinner = confirmBtn.querySelector('.btn-spinner');
        cachedDom = true;
    }

    // ── Open / close ────────────────────────────────────────────────────
    function open() {
        cacheDom();
        currentPreview = null;
        showStep('choose');
        fileInput.value = '';
        dropzoneHint.textContent = t('Contacts.Import.UploadHint');
        modal.classList.remove('d-none');
        document.body.style.overflow = 'hidden';
    }

    function close() {
        if (!modal) return;
        modal.classList.add('d-none');
        document.body.style.overflow = '';
    }
    window.closeImportModal = close;

    function showStep(step) {
        currentStep = step;
        stepChoose.classList.toggle('d-none', step !== 'choose');
        stepPreview.classList.toggle('d-none', step !== 'preview');
        stepResult.classList.toggle('d-none', step !== 'result');

        // Footer buttons adapt to the step.
        if (step === 'choose') {
            confirmBtn.disabled = true;
            confirmLabel.textContent = t('Contacts.Import.Confirm');
            backBtn.textContent = t('Action.Cancel');
        } else if (step === 'preview') {
            confirmLabel.textContent = t('Contacts.Import.Confirm');
            backBtn.textContent = t('Contacts.Import.Back');
            confirmBtn.disabled = !canConfirm();
        } else {
            confirmLabel.textContent = t('Action.Close');
            confirmBtn.disabled = false;
            backBtn.classList.add('d-none');
        }
        if (step !== 'result') backBtn.classList.remove('d-none');
    }

    function canConfirm() {
        if (!currentPreview) return false;
        return currentPreview.validRows > 0;
    }

    // ── Upload + preview ────────────────────────────────────────────────
    async function handleFile(file) {
        if (!file) return;
        const lower = (file.name || '').toLowerCase();
        if (!lower.endsWith('.xlsx') && !lower.endsWith('.xls')) {
            showToast(t('Contacts.Import.OnlyExcel'), 'error');
            return;
        }
        if (file.size > 10 * 1024 * 1024) {
            showToast(t('Contacts.Import.TooLarge'), 'error');
            return;
        }

        dropzoneHint.textContent = t('Contacts.Import.Uploading');

        const fd = new FormData();
        fd.append('file', file);

        try {
            const resp = await fetch('/api/contacts/import/preview', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token() },
                body: fd
            });
            const body = await resp.json().catch(() => null);
            if (!resp.ok) {
                const msg = (body && body.error) || `HTTP ${resp.status}`;
                showToast(msg, 'error');
                dropzoneHint.textContent = t('Contacts.Import.UploadHint');
                return;
            }
            currentPreview = body;
            renderPreview(body);
            showStep('preview');
        } catch (e) {
            showToast(t('Contacts.Import.UploadFailed', e.message), 'error');
            dropzoneHint.textContent = t('Contacts.Import.UploadHint');
        }
    }

    function renderPreview(preview) {
        summaryEl.innerHTML = `
            <span><strong>${preview.totalRows}</strong> ${escapeHtml(t('Contacts.Import.Summary.Total'))}</span>
            <span class="import-summary-ok"><strong>${preview.validRows}</strong> ${escapeHtml(t('Contacts.Import.Summary.Valid'))}</span>
            <span class="import-summary-bad"><strong>${preview.invalidRows}</strong> ${escapeHtml(t('Contacts.Import.Summary.Invalid'))}</span>
            <span class="import-summary-warn"><strong>${preview.duplicateCount}</strong> ${escapeHtml(t('Contacts.Import.Summary.Duplicates'))}</span>
        `;

        // Duplicate strategy section
        const dbDupes = (preview.duplicates || []).filter(d => d.matchesExistingContact).length;
        if (dbDupes > 0) {
            duplicatesSection.classList.remove('d-none');
            duplicatesHint.textContent = t('Contacts.Import.DupsAgainstDb', dbDupes);
        } else {
            duplicatesSection.classList.add('d-none');
        }

        // Per-row error map
        const errorsByRow = {};
        (preview.errors || []).forEach(e => {
            if (!errorsByRow[e.sourceRow]) errorsByRow[e.sourceRow] = [];
            errorsByRow[e.sourceRow].push(e);
        });

        // In-file dupe map
        const inFileDupeRows = new Set(
            (preview.duplicates || []).filter(d => d.matchesEarlierRow).map(d => d.sourceRow)
        );
        const dbDupeRows = new Set(
            (preview.duplicates || []).filter(d => d.matchesExistingContact).map(d => d.sourceRow)
        );

        previewBody.innerHTML = (preview.rows || []).map(row => {
            const errs = errorsByRow[row.sourceRow];
            let statusHtml;
            if (errs && errs.length) {
                statusHtml = `<span class="import-status import-status-bad" title="${escapeHtml(errs.map(e => e.message).join('\n'))}">${escapeHtml(t('Contacts.Import.Row.Invalid'))}</span>`;
            } else if (inFileDupeRows.has(row.sourceRow)) {
                statusHtml = `<span class="import-status import-status-warn">${escapeHtml(t('Contacts.Import.Row.DupInFile'))}</span>`;
            } else if (dbDupeRows.has(row.sourceRow)) {
                statusHtml = `<span class="import-status import-status-warn">${escapeHtml(t('Contacts.Import.Row.DupInDb'))}</span>`;
            } else {
                statusHtml = `<span class="import-status import-status-ok">${escapeHtml(t('Contacts.Import.Row.Ready'))}</span>`;
            }
            const fullName = [row.firstName, row.lastName].filter(Boolean).join(' ');
            return `
                <tr>
                    <td>${row.sourceRow}</td>
                    <td>${escapeHtml(fullName) || '<span class="cell-muted">—</span>'}</td>
                    <td>${escapeHtml(row.phoneNumber || '')}</td>
                    <td>${escapeHtml(row.email || '')}</td>
                    <td>${escapeHtml(row.company || '')}</td>
                    <td>${statusHtml}</td>
                </tr>`;
        }).join('') || `<tr><td colspan="6" class="import-empty">${escapeHtml(t('Contacts.Import.Empty'))}</td></tr>`;

        // Error list
        const flatErrors = preview.errors || [];
        if (flatErrors.length) {
            errorsSection.classList.remove('d-none');
            errorsSummary.textContent = t('Contacts.Import.ErrorsCount', flatErrors.length);
            errorsList.innerHTML = flatErrors.map(e =>
                `<li>${t('Contacts.Import.ErrorLine', e.sourceRow, escapeHtml(e.field), escapeHtml(e.message))}</li>`
            ).join('');
        } else {
            errorsSection.classList.add('d-none');
        }
    }

    // ── Confirm import ──────────────────────────────────────────────────
    async function confirmImport() {
        if (!currentPreview) return;
        const dupStrategy = (document.querySelector('input[name="dupStrategy"]:checked') || {}).value || 'Skip';

        toggleSpinner(true);
        try {
            const resp = await fetch('/api/contacts/import/confirm', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token()
                },
                body: JSON.stringify({
                    rows: currentPreview.rows,
                    duplicateStrategy: dupStrategy
                })
            });
            const body = await resp.json().catch(() => null);
            if (!resp.ok) {
                showToast((body && body.error) || `HTTP ${resp.status}`, 'error');
                return;
            }
            renderResult(body);
            showStep('result');
            // Refresh the underlying table since we just changed it.
            if (typeof window.loadContacts === 'function') window.loadContacts();
        } catch (e) {
            showToast(t('Contacts.Import.ConfirmFailed', e.message), 'error');
        } finally {
            toggleSpinner(false);
        }
    }

    function renderResult(result) {
        resultContent.innerHTML = `
            <h4 class="import-result-title">${escapeHtml(t('Contacts.Import.Result.Title'))}</h4>
            <ul class="import-result-list">
                <li><strong>${result.totalRows}</strong> ${escapeHtml(t('Contacts.Import.Summary.Total'))}</li>
                <li class="import-summary-ok"><strong>${result.imported}</strong> ${escapeHtml(t('Contacts.Import.Result.Imported'))}</li>
                <li class="import-summary-ok"><strong>${result.updated}</strong> ${escapeHtml(t('Contacts.Import.Result.Updated'))}</li>
                <li class="import-summary-warn"><strong>${result.skipped}</strong> ${escapeHtml(t('Contacts.Import.Result.Skipped'))}</li>
                <li class="import-summary-bad"><strong>${(result.failed || []).length}</strong> ${escapeHtml(t('Contacts.Import.Result.Failed'))}</li>
            </ul>`;

        if ((result.failed || []).length) {
            resultErrors.classList.remove('d-none');
            resultErrorsSummary.textContent = t('Contacts.Import.ErrorsCount', result.failed.length);
            resultErrorsList.innerHTML = result.failed.map(e =>
                `<li>${t('Contacts.Import.ErrorLine', e.sourceRow, escapeHtml(e.field), escapeHtml(e.message))}</li>`
            ).join('');
            // Stash the failed list so the "download error report" button on step 3 can rebuild
            // the .xlsx without forcing the user to re-upload.
            resultErrors.dataset.failed = JSON.stringify(result.failed);
        } else {
            resultErrors.classList.add('d-none');
            resultErrors.dataset.failed = '[]';
        }
    }

    function toggleSpinner(busy) {
        confirmBtn.disabled = busy;
        confirmSpinner.classList.toggle('d-none', !busy);
    }

    // ── Error report download ───────────────────────────────────────────
    async function downloadErrorReport(rows, errors) {
        try {
            const resp = await fetch('/api/contacts/import/error-report', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token()
                },
                body: JSON.stringify({ rows, errors })
            });
            if (!resp.ok) {
                showToast(`HTTP ${resp.status}`, 'error');
                return;
            }
            const blob = await resp.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'contact-import-errors.xlsx';
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (e) {
            showToast(t('Contacts.Import.ReportFailed', e.message), 'error');
        }
    }

    // ── Wire up ─────────────────────────────────────────────────────────
    document.addEventListener('click', (e) => {
        if (e.target.closest('#importBtn')) {
            e.preventDefault();
            open();
        }
    });

    document.addEventListener('change', (e) => {
        if (e.target && e.target.id === 'importFileInput') {
            handleFile(e.target.files && e.target.files[0]);
        }
    });

    document.addEventListener('click', (e) => {
        if (e.target.closest('#importBackBtn')) {
            if (currentStep === 'choose') close();
            else if (currentStep === 'preview') showStep('choose');
            else close();
        } else if (e.target.closest('#importConfirmBtn')) {
            if (currentStep === 'result') { close(); return; }
            confirmImport();
        } else if (e.target.closest('#importDownloadErrorsBtn')) {
            if (!currentPreview) return;
            downloadErrorReport(currentPreview.rows, currentPreview.errors);
        } else if (e.target.closest('#importResultDownloadErrorsBtn')) {
            if (!currentPreview) return;
            // Result errors include failed rows from confirm; reuse currentPreview.rows because
            // they are the same row objects the server validated.
            const failed = (resultErrors.dataset.failed && JSON.parse(resultErrors.dataset.failed)) || [];
            downloadErrorReport(currentPreview.rows, failed);
        }
    });

    // Drag-and-drop on the dropzone label.
    function bindDragEvents() {
        cacheDom();
        if (!dropzone) return;
        ['dragenter', 'dragover'].forEach(ev => {
            dropzone.addEventListener(ev, (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropzone.classList.add('is-dragover');
            });
        });
        ['dragleave', 'drop'].forEach(ev => {
            dropzone.addEventListener(ev, (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropzone.classList.remove('is-dragover');
            });
        });
        dropzone.addEventListener('drop', (e) => {
            const file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (file) handleFile(file);
        });
    }
    if (document.readyState !== 'loading') bindDragEvents();
    else document.addEventListener('DOMContentLoaded', bindDragEvents);
})();
