// Per-row AI agent picker for the Contacts page. Clicking the agent chip in any
// row opens a popover listing active agents + an "Unassign" option. Selection
// POSTs to /dashboard/contacts/{id}/agent.
(function () {
    'use strict';

    let cache = null;     // [{id, name, avatarPath, isDefault}]
    let openCtx = null;   // { contactId, anchor }

    function csrf() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''; }
    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    async function loadAgents() {
        if (cache) return cache;
        try {
            const r = await fetch('/dashboard/agents/picker');
            cache = r.ok ? await r.json() : [];
        } catch { cache = []; }
        return cache;
    }

    async function openPopover(anchor) {
        const contactId = Number(anchor.dataset.contactId);
        const currentAgentId = anchor.dataset.currentAgent ? Number(anchor.dataset.currentAgent) : null;
        const agents = await loadAgents();

        const popover = document.getElementById('contactAgentPopover');
        const list = document.getElementById('contactAgentPickerList');
        const empty = document.getElementById('contactAgentPickerEmpty');

        list.innerHTML = '';

        if (!agents.length) {
            empty.classList.remove('d-none');
        } else {
            empty.classList.add('d-none');
            agents.forEach(a => {
                const li = document.createElement('li');
                li.className = (a.id === currentAgentId) ? 'is-selected' : '';
                const avatar = a.avatarPath
                    ? `<img src="${escapeHtml(a.avatarPath)}" alt="" />`
                    : '🤖';
                li.innerHTML = `
                    <span class="ag-picker-row-avatar">${avatar}</span>
                    <span class="ag-picker-row-name">${escapeHtml(a.name)}</span>
                    ${a.isDefault ? `<span class="ag-picker-row-default">${escapeHtml(t('Agents.Status.Default'))}</span>` : ''}
                `;
                li.addEventListener('click', () => assign(contactId, a.id));
                list.appendChild(li);
            });
        }

        // Unassign option (always present).
        const unassign = document.createElement('li');
        unassign.className = 'is-unassign' + (currentAgentId == null ? ' is-selected' : '');
        unassign.innerHTML = `<span class="ag-picker-row-name">${escapeHtml(t('Agents.Picker.Unassign'))}</span>`;
        unassign.addEventListener('click', () => assign(contactId, null));
        list.appendChild(unassign);

        const r = anchor.getBoundingClientRect();
        popover.style.top = (r.bottom + 6) + 'px';
        popover.style.left = (r.left) + 'px';
        popover.classList.remove('d-none');
        openCtx = { contactId, anchor };
    }

    function closePopover() {
        document.getElementById('contactAgentPopover')?.classList.add('d-none');
        openCtx = null;
    }

    async function assign(contactId, agentId) {
        try {
            const r = await fetch(`/dashboard/contacts/${contactId}/agent`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': csrf()
                },
                body: JSON.stringify({ agentId })
            });
            const payload = await r.json().catch(() => ({}));
            if (!r.ok) {
                alert(payload.error || 'Failed to assign agent.');
                return;
            }
            closePopover();
            updateChip(contactId, payload);
        } catch (e) {
            alert('Network error: ' + e.message);
        }
    }

    function updateChip(contactId, dto) {
        const btn = document.querySelector(`[data-agent-picker="contact"][data-contact-id="${contactId}"]`);
        if (!btn) return;
        btn.dataset.currentAgent = dto.agentId ?? '';
        const avatarEl = btn.querySelector('.chat-agent-picker-avatar');
        const nameEl = btn.querySelector('.chat-agent-picker-name');
        if (dto.agentId) {
            avatarEl.innerHTML = dto.agentAvatarPath
                ? `<img src="${escapeHtml(dto.agentAvatarPath)}" alt="" />`
                : '🤖';
            nameEl.textContent = dto.agentName;
            nameEl.style.color = '';
        } else {
            avatarEl.textContent = '🤖';
            nameEl.textContent = t('Agents.Picker.Unassign');
            nameEl.style.color = 'var(--text-muted)';
        }
    }

    // Delegate click for any current/future agent-picker chip in the contacts table.
    document.addEventListener('click', e => {
        const chip = e.target.closest('[data-agent-picker="contact"]');
        if (chip) {
            e.stopPropagation();
            if (openCtx && openCtx.anchor === chip) closePopover();
            else openPopover(chip);
            return;
        }
        // Click outside → close.
        const popover = document.getElementById('contactAgentPopover');
        if (popover && !popover.classList.contains('d-none') && !popover.contains(e.target)) {
            closePopover();
        }
    });

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') closePopover();
    });
})();
