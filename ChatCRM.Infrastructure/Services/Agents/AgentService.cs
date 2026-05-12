using System.Text.Json;
using ChatCRM.Application.Agents.DTOs;
using ChatCRM.Application.Agents.Exceptions;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Agents
{
    /// <summary>
    /// AI Agent CRUD + invariant enforcement. Uses a short transaction for set-default to
    /// avoid a partial-update window where two rows are simultaneously default (the filtered
    /// unique index would catch it, but a clean rollback + clear error is friendlier).
    /// </summary>
    public sealed class AgentService : IAgentService
    {
        private const int DefaultWorkspaceId = 1;

        private readonly AppDbContext _db;
        private readonly ILogger<AgentService> _logger;

        public AgentService(AppDbContext db, ILogger<AgentService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<AgentListItemDto>> ListAsync(
            AgentListFilter filter, string? searchTerm, CancellationToken ct = default)
        {
            var q = _db.Agents.AsNoTracking()
                .Where(a => a.WorkspaceId == DefaultWorkspaceId);

            q = filter switch
            {
                AgentListFilter.Active   => q.Where(a => a.IsActive),
                AgentListFilter.Inactive => q.Where(a => !a.IsActive),
                _                        => q
            };

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var s = searchTerm.Trim();
                q = q.Where(a => a.Name.Contains(s) || (a.Description != null && a.Description.Contains(s)));
            }

            // Default first, then active alphabetical, then inactive alphabetical.
            var rows = await q
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.IsActive)
                .ThenBy(a => a.Name)
                .Select(a => new
                {
                    a.Id, a.Name, a.Description, a.AvatarPath, a.Instructions,
                    a.IsDefault, a.IsActive, a.CreatedAt, a.UpdatedAt,
                    CreatedBy = a.CreatedByUser,
                    // Contacts assigned to this agent (per-contact preference) + conversations
                    // routed to it (per-conversation). Both surface as stat tiles on the card.
                    ContactsCount = _db.WhatsAppContacts.Count(c => c.AssignedAgentId == a.Id),
                    AssignedCount = _db.Conversations.Count(c => c.AssignedAgentId == a.Id)
                })
                .ToListAsync(ct);

            return rows.Select(r => new AgentListItemDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                AvatarPath = r.AvatarPath,
                Instructions = r.Instructions,
                IsDefault = r.IsDefault,
                IsActive = r.IsActive,
                CreatedByName = r.CreatedBy is null ? null
                    : (string.IsNullOrWhiteSpace(r.CreatedBy.FirstName)
                        ? r.CreatedBy.Email
                        : (r.CreatedBy.FirstName + " " + r.CreatedBy.LastName).Trim()),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                AssignedConversationsCount = r.AssignedCount,
                AssignedContactsCount = r.ContactsCount
            }).ToList();
        }

        public async Task<AgentDetailDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var rows = await ListAsync(AgentListFilter.All, null, ct);
            var row = rows.FirstOrDefault(a => a.Id == id);
            return row is null ? null : new AgentDetailDto
            {
                Id = row.Id, Name = row.Name, Description = row.Description,
                AvatarPath = row.AvatarPath, Instructions = row.Instructions,
                IsDefault = row.IsDefault, IsActive = row.IsActive,
                CreatedByName = row.CreatedByName, CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt,
                AssignedConversationsCount = row.AssignedConversationsCount,
                AssignedContactsCount = row.AssignedContactsCount
            };
        }

        public async Task<AgentDetailDto> CreateAsync(AgentUpsertDto dto, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateUpsert(dto);

            var name = dto.Name.Trim();
            var nameTaken = await _db.Agents.AnyAsync(
                a => a.WorkspaceId == DefaultWorkspaceId && a.Name == name, ct);
            if (nameTaken)
                throw new AgentValidationException(new Dictionary<string, string>
                {
                    ["Name"] = "Agents.Validation.NameTaken"
                });

            var nowUtc = DateTime.UtcNow;

            // First agent in the workspace? Auto-default. Otherwise honour MakeDefault but
            // we'll still need to demote the previous default — done via SetDefaultAsync after
            // creation so the same atomic helper runs.
            var workspaceHasAgents = await _db.Agents.AnyAsync(a => a.WorkspaceId == DefaultWorkspaceId, ct);
            var shouldBeDefaultOnCreate = !workspaceHasAgents;

            var entity = new Agent
            {
                WorkspaceId = DefaultWorkspaceId,
                Name = name,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Instructions = string.IsNullOrWhiteSpace(dto.Instructions) ? null : dto.Instructions,
                IsActive = dto.IsActive,
                IsDefault = shouldBeDefaultOnCreate,
                CreatedByUserId = actingUserId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            // Default-must-be-active: if user is asking for default at creation but inactive,
            // refuse — better to surface than silently flip.
            if ((shouldBeDefaultOnCreate || dto.MakeDefault) && !entity.IsActive)
                throw AgentInvariantException.InactiveCannotBeDefault();

            _db.Agents.Add(entity);
            await _db.SaveChangesAsync(ct);

            // Honour explicit MakeDefault when there were already agents.
            if (dto.MakeDefault && !shouldBeDefaultOnCreate)
                await PromoteToDefaultInternalAsync(entity.Id, actingUserId, ct);

            WriteAudit(actingUserId, "agent.created", entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new { entity.Name, entity.IsActive, entity.IsDefault }));
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[AGENT] Created {Id} '{Name}' by {User}", entity.Id, name, actingUserId);
            return (await GetAsync(entity.Id, ct))!;
        }

        public async Task<AgentDetailDto> UpdateAsync(int id, AgentUpsertDto dto, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateUpsert(dto);

            var entity = await _db.Agents
                .FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException("Agent not found.");

            var name = dto.Name.Trim();
            if (entity.Name != name)
            {
                var nameTaken = await _db.Agents.AnyAsync(
                    a => a.WorkspaceId == DefaultWorkspaceId && a.Id != id && a.Name == name, ct);
                if (nameTaken)
                    throw new AgentValidationException(new Dictionary<string, string>
                    {
                        ["Name"] = "Agents.Validation.NameTaken"
                    });
            }

            // Default must remain active — only when the user wants to KEEP it as default.
            // Demote-and-deactivate in the same call is allowed (handled below).
            if (entity.IsDefault && !dto.IsActive && dto.MakeDefault)
                throw AgentInvariantException.DefaultMustBeActive();
            // Promoting to default while inactive is rejected.
            if (dto.MakeDefault && !dto.IsActive)
                throw AgentInvariantException.InactiveCannotBeDefault();

            var beforeJson = JsonSerializer.Serialize(new
            {
                entity.Name, entity.Description, entity.IsActive, entity.IsDefault
            });

            entity.Name = name;
            entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            entity.Instructions = string.IsNullOrWhiteSpace(dto.Instructions) ? null : dto.Instructions;
            entity.IsActive = dto.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;

            WriteAudit(actingUserId, "agent.updated", entity.Id.ToString(),
                beforeJson: beforeJson,
                afterJson: JsonSerializer.Serialize(new { entity.Name, entity.Description, entity.IsActive, InstructionsLen = entity.Instructions?.Length ?? 0 }));

            await _db.SaveChangesAsync(ct);

            // Default flip — handled after the basic save so the demote/promote helper sees
            // a consistent state. The filtered unique index ((WorkspaceId) WHERE IsDefault=1)
            // is the final guardrail against two rows holding the bit simultaneously.
            if (dto.MakeDefault && !entity.IsDefault)
            {
                // Promote: PromoteToDefaultInternalAsync demotes any existing default + sets
                // this one inside a transaction.
                await PromoteToDefaultInternalAsync(entity.Id, actingUserId, ct);
            }
            else if (!dto.MakeDefault && entity.IsDefault)
            {
                // Demote: workspace temporarily has no default until another agent is promoted.
                // Auto-assignment for new conversations will skip — that's intentional.
                entity.IsDefault = false;
                entity.UpdatedAt = DateTime.UtcNow;
                WriteAudit(actingUserId, "agent.unset_default", entity.Id.ToString(),
                    afterJson: JsonSerializer.Serialize(new { entity.Name }));
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("[AGENT] Demoted {Id} '{Name}' from default by {User}.",
                    entity.Id, entity.Name, actingUserId);
            }

            return (await GetAsync(id, ct))!;
        }

        public async Task DeleteAsync(int id, string actingUserId, CancellationToken ct = default)
        {
            var entity = await _db.Agents
                .FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException("Agent not found.");

            if (entity.IsDefault)
                throw AgentInvariantException.DefaultMustExistFirst();

            // Conversations assigned to this agent get the FK SetNull treatment automatically
            // via the EF cascade rule; we don't reassign them here.
            _db.Agents.Remove(entity);

            WriteAudit(actingUserId, "agent.deleted", entity.Id.ToString(),
                beforeJson: JsonSerializer.Serialize(new { entity.Name, entity.IsActive }));

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[AGENT] Deleted {Id} '{Name}' by {User}", entity.Id, entity.Name, actingUserId);
        }

        public async Task<AgentDetailDto> SetDefaultAsync(int id, string actingUserId, CancellationToken ct = default)
        {
            await PromoteToDefaultInternalAsync(id, actingUserId, ct);
            return (await GetAsync(id, ct))!;
        }

        private async Task PromoteToDefaultInternalAsync(int id, string actingUserId, CancellationToken ct)
        {
            var entity = await _db.Agents
                .FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException("Agent not found.");

            if (!entity.IsActive)
                throw AgentInvariantException.InactiveCannotBeDefault();

            if (entity.IsDefault) return; // no-op idempotent

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Demote the current default (if any) — there's at most one due to the filtered
                // unique index. UpdateAsync would be ambiguous here, so we touch all matching
                // rows to be safe.
                var currentDefaults = await _db.Agents
                    .Where(a => a.WorkspaceId == DefaultWorkspaceId && a.IsDefault && a.Id != id)
                    .ToListAsync(ct);
                foreach (var cur in currentDefaults)
                {
                    cur.IsDefault = false;
                    cur.UpdatedAt = DateTime.UtcNow;
                }
                // SaveChanges between demote and promote so the unique index is satisfied at
                // every intermediate state.
                if (currentDefaults.Count > 0) await _db.SaveChangesAsync(ct);

                entity.IsDefault = true;
                entity.UpdatedAt = DateTime.UtcNow;

                WriteAudit(actingUserId, "agent.set_default", entity.Id.ToString(),
                    afterJson: JsonSerializer.Serialize(new { entity.Name, PreviousDefaultIds = currentDefaults.Select(c => c.Id) }));

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<AgentDetailDto> SetActiveAsync(int id, bool isActive, string actingUserId, CancellationToken ct = default)
        {
            var entity = await _db.Agents
                .FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException("Agent not found.");

            if (entity.IsDefault && !isActive)
                throw AgentInvariantException.DefaultMustBeActive();

            if (entity.IsActive == isActive) return (await GetAsync(id, ct))!; // no-op

            entity.IsActive = isActive;
            entity.UpdatedAt = DateTime.UtcNow;

            WriteAudit(actingUserId, isActive ? "agent.activated" : "agent.deactivated",
                entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new { entity.Name }));

            await _db.SaveChangesAsync(ct);
            return (await GetAsync(id, ct))!;
        }

        public async Task<AgentDetailDto> SetAvatarAsync(int id, string avatarPath, string actingUserId, CancellationToken ct = default)
        {
            var entity = await _db.Agents
                .FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException("Agent not found.");

            entity.AvatarPath = string.IsNullOrWhiteSpace(avatarPath) ? null : avatarPath;
            entity.UpdatedAt = DateTime.UtcNow;

            WriteAudit(actingUserId, "agent.avatar_set", entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new { AvatarPath = entity.AvatarPath }));

            await _db.SaveChangesAsync(ct);
            return (await GetAsync(id, ct))!;
        }

        public async Task<List<AgentPickerItemDto>> ListActiveForPickerAsync(CancellationToken ct = default)
        {
            return await _db.Agents.AsNoTracking()
                .Where(a => a.WorkspaceId == DefaultWorkspaceId && a.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .ThenBy(a => a.Name)
                .Select(a => new AgentPickerItemDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    AvatarPath = a.AvatarPath,
                    IsDefault = a.IsDefault
                })
                .ToListAsync(ct);
        }

        public async Task<int?> GetDefaultAgentIdAsync(CancellationToken ct = default)
        {
            // Filtered index makes this a single-row seek.
            return await _db.Agents.AsNoTracking()
                .Where(a => a.WorkspaceId == DefaultWorkspaceId && a.IsDefault && a.IsActive)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<ContactAgentAssignmentDto> AssignContactAsync(
            int contactId, int? agentId, string actingUserId, CancellationToken ct = default)
        {
            var contact = await _db.WhatsAppContacts
                .FirstOrDefaultAsync(c => c.Id == contactId, ct)
                ?? throw new InvalidOperationException("Contact not found.");

            Agent? agent = null;
            if (agentId.HasValue)
            {
                agent = await _db.Agents
                    .FirstOrDefaultAsync(a => a.Id == agentId.Value && a.WorkspaceId == DefaultWorkspaceId, ct)
                    ?? throw new InvalidOperationException("Agent not found.");
                if (!agent.IsActive)
                    throw new InvalidOperationException("Cannot assign an inactive agent.");
            }

            var beforeId = contact.AssignedAgentId;
            contact.AssignedAgentId = agentId;

            WriteAudit(actingUserId, "contact.agent_assigned", contact.Id.ToString(),
                beforeJson: JsonSerializer.Serialize(new { AgentId = beforeId }),
                afterJson: JsonSerializer.Serialize(new { AgentId = agentId }));

            await _db.SaveChangesAsync(ct);

            return new ContactAgentAssignmentDto
            {
                ContactId = contact.Id,
                AgentId = agent?.Id,
                AgentName = agent?.Name,
                AgentAvatarPath = agent?.AvatarPath
            };
        }

        public async Task ReassignConversationAsync(int conversationId, int? agentId, string actingUserId, CancellationToken ct = default)
        {
            var conv = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
                ?? throw new InvalidOperationException("Conversation not found.");

            if (agentId.HasValue)
            {
                var agent = await _db.Agents
                    .FirstOrDefaultAsync(a => a.Id == agentId.Value && a.WorkspaceId == DefaultWorkspaceId, ct)
                    ?? throw new InvalidOperationException("Agent not found.");
                if (!agent.IsActive)
                    throw new InvalidOperationException("Cannot assign an inactive agent to a conversation.");
            }

            var beforeId = conv.AssignedAgentId;
            conv.AssignedAgentId = agentId;

            WriteAudit(actingUserId, "conversation.agent_reassigned", conv.Id.ToString(),
                beforeJson: JsonSerializer.Serialize(new { AgentId = beforeId }),
                afterJson: JsonSerializer.Serialize(new { AgentId = agentId }));

            await _db.SaveChangesAsync(ct);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static void ValidateUpsert(AgentUpsertDto dto)
        {
            var errors = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(dto.Name))
                errors["Name"] = "Agents.Validation.NameRequired";
            else if (dto.Name.Trim().Length > 80)
                errors["Name"] = "Agents.Validation.NameTooLong";

            if (!string.IsNullOrEmpty(dto.Description) && dto.Description.Length > 500)
                errors["Description"] = "Agents.Validation.DescriptionTooLong";

            if (!string.IsNullOrEmpty(dto.Instructions) && dto.Instructions.Length > 8000)
                errors["Instructions"] = "Agents.Validation.InstructionsTooLong";

            if (errors.Count > 0)
                throw new AgentValidationException(errors);
        }

        private void WriteAudit(string? actingUserId, string action, string entityId,
            string? beforeJson = null, string? afterJson = null)
        {
            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = string.IsNullOrEmpty(actingUserId) ? "system" : $"user:{actingUserId}",
                Action = action,
                EntityType = nameof(Agent),
                EntityId = entityId,
                BeforeJson = beforeJson,
                AfterJson = afterJson,
                AtUtc = DateTime.UtcNow
            });
        }
    }
}
