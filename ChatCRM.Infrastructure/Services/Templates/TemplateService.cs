using System.Text.Json;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Templates;
using ChatCRM.Application.Templates.DTOs;
using ChatCRM.Application.Templates.Exceptions;
using ChatCRM.Application.Templates.Validation;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Templates
{
    /// <summary>
    /// Application-layer orchestrator for the template manager. Handles validation, rate
    /// limiting, persistence, audit logging, and the manual Meta reconciliation. The provider
    /// is the only thing that actually talks to Meta; this service decides when to call it
    /// and how to react to the response.
    /// </summary>
    public sealed class TemplateService : ITemplateService
    {
        private const int DefaultWorkspaceId = 1;
        private const int SubmissionsPerHourLimit = 10;
        private static readonly TimeSpan SubmissionWindow = TimeSpan.FromHours(1);
        private const string SubmissionRateCacheKey = "templates:submit:rate:1";

        private readonly AppDbContext _db;
        private readonly IWhatsAppTemplateProvider _provider;
        private readonly ITemplateProviderHealth _health;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TemplateService> _logger;

        public TemplateService(
            AppDbContext db,
            IWhatsAppTemplateProvider provider,
            ITemplateProviderHealth health,
            IMemoryCache cache,
            ILogger<TemplateService> logger)
        {
            _db = db;
            _provider = provider;
            _health = health;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<TemplateListItemDto>> ListAsync(bool archived, CancellationToken ct = default)
        {
            return await _db.WhatsAppTemplates
                .AsNoTracking()
                .Where(t => t.WorkspaceId == DefaultWorkspaceId
                         && (archived
                             ? t.Status == WhatsAppTemplateStatus.Disabled
                             : t.Status != WhatsAppTemplateStatus.Disabled))
                .OrderByDescending(t => t.UpdatedAt)
                .Select(t => new TemplateListItemDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Category = t.Category,
                    LanguageCode = t.LanguageCode,
                    Status = t.Status,
                    RejectionReason = t.RejectionReason,
                    SubmittedAt = t.SubmittedAt,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt,
                    CreatedByName = t.CreatedByUser != null
                        ? (string.IsNullOrWhiteSpace(t.CreatedByUser.FirstName)
                            ? t.CreatedByUser.Email
                            : (t.CreatedByUser.FirstName + " " + t.CreatedByUser.LastName).Trim())
                        : null
                })
                .ToListAsync(ct);
        }

        public async Task<TemplateDetailDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.WhatsAppTemplates
                .AsNoTracking()
                .Include(x => x.SubmittedViaInstance)
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == DefaultWorkspaceId, ct);
            return t is null ? null : ToDetail(t);
        }

        public async Task<TemplateDetailDto> CreateDraftAsync(CreateTemplateDto dto, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var name = (dto.Name ?? string.Empty).Trim().ToLowerInvariant();
            var lang = (dto.LanguageCode ?? string.Empty).Trim();
            var body = dto.Body ?? string.Empty;
            var footer = string.IsNullOrWhiteSpace(dto.Footer) ? null : dto.Footer.Trim();
            var category = (MetaMessageCategory)dto.Category;

            // Defensive: Service category is a free-form runtime classification, not a template
            // category. The validator only checks the language; reject Service here so we don't
            // create unsubmittable rows.
            if (category == MetaMessageCategory.Service)
                throw new TemplateValidationException(new Dictionary<string, string>
                {
                    ["Category"] = "Templates.Validation.CategoryServiceNotAllowed"
                });

            TemplateContentValidator.Validate(name, lang, body, footer);

            // Local uniqueness check before the unique-index trip so we can return a friendly
            // field error instead of a SqlException.
            var nameTaken = await _db.WhatsAppTemplates.AnyAsync(
                t => t.WorkspaceId == DefaultWorkspaceId
                  && t.Name == name && t.LanguageCode == lang, ct);
            if (nameTaken)
                throw new TemplateValidationException(new Dictionary<string, string>
                {
                    ["Name"] = "Templates.Validation.NameTaken"
                });

            var nowUtc = DateTime.UtcNow;
            var entity = new WhatsAppTemplate
            {
                WorkspaceId = DefaultWorkspaceId,
                Name = name,
                Category = category,
                LanguageCode = lang,
                Body = body,
                Footer = footer,
                Status = WhatsAppTemplateStatus.Draft,
                CreatedByUserId = actingUserId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            _db.WhatsAppTemplates.Add(entity);
            await _db.SaveChangesAsync(ct);

            WriteAudit(actingUserId, "templates.draft.created", entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new { entity.Name, entity.LanguageCode, entity.Category, entity.Body, entity.Footer }));
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("[TEMPLATES] Draft {Id} '{Name}' ({Lang}) created by {User}", entity.Id, name, lang, actingUserId);
            return ToDetail(entity);
        }

        public async Task<TemplateDetailDto> UpdateDraftAsync(int id, UpdateTemplateDto dto, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var entity = await _db.WhatsAppTemplates
                .FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException($"Template {id} not found.");

            // Once Meta has seen the template (anything beyond Draft), edits are forbidden —
            // Meta forbids in-place edits and we'd lie about state if we let users mutate.
            if (entity.Status != WhatsAppTemplateStatus.Draft && entity.Status != WhatsAppTemplateStatus.Rejected)
                throw new InvalidOperationException("Only Draft and Rejected templates can be edited.");

            var name = (dto.Name ?? string.Empty).Trim().ToLowerInvariant();
            var lang = (dto.LanguageCode ?? string.Empty).Trim();
            var body = dto.Body ?? string.Empty;
            var footer = string.IsNullOrWhiteSpace(dto.Footer) ? null : dto.Footer.Trim();
            var category = (MetaMessageCategory)dto.Category;

            if (category == MetaMessageCategory.Service)
                throw new TemplateValidationException(new Dictionary<string, string>
                {
                    ["Category"] = "Templates.Validation.CategoryServiceNotAllowed"
                });

            TemplateContentValidator.Validate(name, lang, body, footer);

            // If the (Name, Language) tuple changed, re-check uniqueness against other rows.
            if (entity.Name != name || entity.LanguageCode != lang)
            {
                var nameTaken = await _db.WhatsAppTemplates.AnyAsync(
                    t => t.WorkspaceId == DefaultWorkspaceId
                      && t.Id != entity.Id
                      && t.Name == name && t.LanguageCode == lang, ct);
                if (nameTaken)
                    throw new TemplateValidationException(new Dictionary<string, string>
                    {
                        ["Name"] = "Templates.Validation.NameTaken"
                    });
            }

            var beforeJson = JsonSerializer.Serialize(new { entity.Name, entity.LanguageCode, entity.Category, entity.Body, entity.Footer });

            entity.Name = name;
            entity.Category = category;
            entity.LanguageCode = lang;
            entity.Body = body;
            entity.Footer = footer;

            // Editing a rejected template returns it to Draft so the next submit is a fresh attempt.
            if (entity.Status == WhatsAppTemplateStatus.Rejected)
            {
                entity.Status = WhatsAppTemplateStatus.Draft;
                entity.RejectionReason = null;
            }
            entity.UpdatedAt = DateTime.UtcNow;

            WriteAudit(actingUserId, "templates.draft.updated", entity.Id.ToString(),
                beforeJson: beforeJson,
                afterJson: JsonSerializer.Serialize(new { entity.Name, entity.LanguageCode, entity.Category, entity.Body, entity.Footer }));

            await _db.SaveChangesAsync(ct);
            return ToDetail(entity);
        }

        public async Task<TemplateDetailDto> SubmitAsync(int id, int? submittedViaInstanceId, string actingUserId, CancellationToken ct = default)
        {
            if (!_provider.IsConfigured)
                throw new TemplateProviderNotConfiguredException();

            var entity = await _db.WhatsAppTemplates
                .FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException($"Template {id} not found.");

            if (entity.Status != WhatsAppTemplateStatus.Draft && entity.Status != WhatsAppTemplateStatus.Rejected)
                throw new InvalidOperationException("Only Draft or Rejected templates can be submitted.");

            // Re-validate before sending — content might have been mutated by another path.
            TemplateContentValidator.Validate(entity.Name, entity.LanguageCode, entity.Body, entity.Footer);

            // Resolve submitting instance: explicit choice, else the first connected Business one.
            int? viaId = submittedViaInstanceId;
            if (viaId is null)
            {
                viaId = await _db.WhatsAppInstances
                    .Where(i => i.Integration == WhatsAppIntegration.Business)
                    .OrderByDescending(i => i.Status == InstanceStatus.Connected)
                    .ThenByDescending(i => i.LastConnectedAt)
                    .Select(i => (int?)i.Id)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                var ok = await _db.WhatsAppInstances.AnyAsync(
                    i => i.Id == viaId.Value && i.Integration == WhatsAppIntegration.Business, ct);
                if (!ok) throw new InvalidOperationException("Selected instance is not a WhatsApp Business instance.");
            }

            EnforceSubmissionRateLimit();

            TemplateProviderSubmitResult result;
            try
            {
                result = await _provider.SubmitAsync(
                    entity.Name, entity.LanguageCode, entity.Category, entity.Body, entity.Footer, ct);
            }
            catch (TemplateAuthenticationException ex)
            {
                _health.RecordAuthFailure(ex.Message);
                WriteAudit(actingUserId, "templates.auth.failed", entity.Id.ToString(),
                    afterJson: JsonSerializer.Serialize(new { ErrorCode = ex.ErrorCode, ex.Message }));
                await _db.SaveChangesAsync(ct);
                throw;
            }

            // Provider call succeeded — clear any prior auth-failure flag.
            _health.Reset();

            var nowUtc = DateTime.UtcNow;
            entity.SubmittedViaInstanceId = viaId;
            entity.SubmittedAt = nowUtc;
            entity.StatusCheckedAt = nowUtc;
            entity.UpdatedAt = nowUtc;

            if (result.Success)
            {
                entity.MetaTemplateId = result.MetaTemplateId;
                entity.Status = result.MappedStatus ?? WhatsAppTemplateStatus.Submitted;
                entity.RejectionReason = null;
                RecordSubmissionTimestamp();
            }
            else
            {
                entity.Status = WhatsAppTemplateStatus.Rejected;
                entity.RejectionReason = result.FailureReason ?? "Meta rejected the submission.";
            }

            WriteAudit(actingUserId,
                result.Success ? "templates.submitted" : "templates.submit.rejected",
                entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new
                {
                    MetaTemplateId = result.MetaTemplateId,
                    RawStatus = result.RawStatus,
                    Status = entity.Status.ToString(),
                    Reason = entity.RejectionReason,
                    ViaInstanceId = viaId
                }));

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[TEMPLATES] Submit {Id} '{Name}' → status={Status} metaId={MetaId}",
                entity.Id, entity.Name, entity.Status, result.MetaTemplateId);

            return ToDetail(entity);
        }

        public async Task SoftDeleteAsync(int id, string actingUserId, CancellationToken ct = default)
        {
            var entity = await _db.WhatsAppTemplates
                .FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException($"Template {id} not found.");

            if (entity.Status == WhatsAppTemplateStatus.Disabled)
                return; // already archived — idempotent

            // Drafts never made it to Meta — no remote call needed.
            var hadRemote = entity.Status != WhatsAppTemplateStatus.Draft && _provider.IsConfigured;
            if (hadRemote)
            {
                try
                {
                    await _provider.DeleteAsync(entity.Name, ct);
                }
                catch (TemplateAuthenticationException ex)
                {
                    _health.RecordAuthFailure(ex.Message);
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    // Transient — log but proceed with the local soft-delete. The next "Sync from
                    // Meta" run will reconcile any drift.
                    _logger.LogWarning(ex, "[TEMPLATES] Remote delete failed for {Name}; soft-deleting locally anyway.", entity.Name);
                }
            }

            entity.Status = WhatsAppTemplateStatus.Disabled;
            entity.UpdatedAt = DateTime.UtcNow;

            WriteAudit(actingUserId, "templates.deleted", entity.Id.ToString(),
                afterJson: JsonSerializer.Serialize(new { entity.Name, entity.LanguageCode, RemoteDeleted = hadRemote }));

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[TEMPLATES] Soft-deleted {Id} '{Name}' (remote={Remote})", entity.Id, entity.Name, hadRemote);
        }

        public async Task<TemplateSyncResult> SyncFromMetaAsync(string actingUserId, CancellationToken ct = default)
        {
            if (!_provider.IsConfigured)
                throw new TemplateProviderNotConfiguredException();

            IReadOnlyList<TemplateProviderRemoteRow> remoteRows;
            try
            {
                remoteRows = await _provider.ListAllAsync(ct);
            }
            catch (TemplateAuthenticationException ex)
            {
                _health.RecordAuthFailure(ex.Message);
                throw;
            }

            _health.Reset();
            var nowUtc = DateTime.UtcNow;

            var locals = await _db.WhatsAppTemplates
                .Where(t => t.WorkspaceId == DefaultWorkspaceId)
                .ToListAsync(ct);

            var byKey = locals.ToDictionary(
                l => (l.Name, l.LanguageCode),
                StructuralKey.Comparer);

            var result = new TemplateSyncResult { RemoteCount = remoteRows.Count };
            var seenKeys = new HashSet<(string, string)>(StructuralKey.Comparer);

            foreach (var row in remoteRows)
            {
                var key = (row.Name, row.LanguageCode);
                seenKeys.Add(key);

                if (byKey.TryGetValue(key, out var local))
                {
                    var changed = false;
                    if (local.Status != row.MappedStatus)
                    {
                        local.Status = row.MappedStatus;
                        changed = true;
                    }
                    if (local.MetaTemplateId != row.MetaTemplateId && !string.IsNullOrEmpty(row.MetaTemplateId))
                    {
                        local.MetaTemplateId = row.MetaTemplateId;
                        changed = true;
                    }
                    if (local.RejectionReason != row.Reason)
                    {
                        local.RejectionReason = row.Reason;
                        changed = true;
                    }
                    if (changed)
                    {
                        local.StatusCheckedAt = nowUtc;
                        local.UpdatedAt = nowUtc;
                        result.LocalUpdated++;
                    }
                }
                else
                {
                    // Imported row — Meta knows about it but we don't. Bring it in as-is; Body
                    // and Category fall back to safe defaults if Meta didn't return them.
                    var imported = new WhatsAppTemplate
                    {
                        WorkspaceId = DefaultWorkspaceId,
                        Name = row.Name,
                        LanguageCode = row.LanguageCode,
                        Category = row.Category ?? MetaMessageCategory.Utility,
                        Body = row.Body ?? "(imported — body unavailable)",
                        Footer = row.Footer,
                        Status = row.MappedStatus,
                        MetaTemplateId = row.MetaTemplateId,
                        RejectionReason = row.Reason,
                        SubmittedAt = nowUtc,
                        StatusCheckedAt = nowUtc,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };
                    _db.WhatsAppTemplates.Add(imported);
                    result.LocalImported++;
                    result.Notes.Add($"Imported '{row.Name}' ({row.LanguageCode}).");
                }
            }

            // Locally-known but Meta-unknown: orphans. Mark Disabled (don't delete the row;
            // keeps audit history). Skip drafts — Meta never knew about them.
            foreach (var local in locals)
            {
                if (local.Status == WhatsAppTemplateStatus.Draft) continue;
                if (local.Status == WhatsAppTemplateStatus.Disabled) continue;
                if (seenKeys.Contains((local.Name, local.LanguageCode))) continue;

                local.Status = WhatsAppTemplateStatus.Disabled;
                local.RejectionReason = "Disappeared from Meta — orphaned by sync.";
                local.UpdatedAt = nowUtc;
                result.LocalOrphaned++;
                result.Notes.Add($"Orphaned '{local.Name}' ({local.LanguageCode}).");
            }

            WriteAudit(actingUserId, "templates.synced", "*",
                afterJson: JsonSerializer.Serialize(new
                {
                    result.RemoteCount, result.LocalUpdated, result.LocalImported, result.LocalOrphaned
                }));

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[TEMPLATES] Sync done: remote={Remote} updated={Upd} imported={Imp} orphaned={Orph}",
                result.RemoteCount, result.LocalUpdated, result.LocalImported, result.LocalOrphaned);

            return result;
        }

        public async Task<List<TemplatePickerItemDto>> ListApprovedForPickerAsync(CancellationToken ct = default)
        {
            var rows = await _db.WhatsAppTemplates
                .AsNoTracking()
                .Where(t => t.WorkspaceId == DefaultWorkspaceId
                         && t.Status == WhatsAppTemplateStatus.Approved)
                .OrderBy(t => t.Name)
                .ThenBy(t => t.LanguageCode)
                .Select(t => new { t.Id, t.Name, t.Category, t.LanguageCode, t.Body, t.Footer })
                .ToListAsync(ct);

            return rows.Select(r => new TemplatePickerItemDto
            {
                Id = r.Id,
                Name = r.Name,
                Category = r.Category,
                LanguageCode = r.LanguageCode,
                Body = r.Body,
                Footer = r.Footer,
                VariableCount = TemplateContentValidator.CountVariables(r.Body)
            }).ToList();
        }

        public async Task<TemplateProviderStateDto> GetProviderStateAsync(CancellationToken ct = default)
        {
            var hasBusinessInstance = await _db.WhatsAppInstances
                .AnyAsync(i => i.Integration == WhatsAppIntegration.Business, ct);

            var submissions = ReadSubmissionTimestamps()
                .Count(t => DateTime.UtcNow - t < SubmissionWindow);

            return new TemplateProviderStateDto
            {
                IsConfigured = _provider.IsConfigured,
                HasAuthFailure = _health.LastAuthFailureAtUtc.HasValue,
                LastAuthFailureAtUtc = _health.LastAuthFailureAtUtc,
                LastAuthFailureMessage = _health.LastAuthFailureMessage,
                HasBusinessInstance = hasBusinessInstance,
                SubmissionsLastHour = submissions,
                SubmissionsLimitPerHour = SubmissionsPerHourLimit
            };
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static TemplateDetailDto ToDetail(WhatsAppTemplate t) => new()
        {
            Id = t.Id,
            Name = t.Name,
            Category = t.Category,
            LanguageCode = t.LanguageCode,
            Body = t.Body,
            Footer = t.Footer,
            Status = t.Status,
            MetaTemplateId = t.MetaTemplateId,
            RejectionReason = t.RejectionReason,
            SubmittedViaInstanceId = t.SubmittedViaInstanceId,
            SubmittedViaInstanceName = t.SubmittedViaInstance?.DisplayName,
            SubmittedAt = t.SubmittedAt,
            StatusCheckedAt = t.StatusCheckedAt,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };

        private void WriteAudit(string? actingUserId, string action, string entityId,
            string? beforeJson = null, string? afterJson = null)
        {
            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = string.IsNullOrEmpty(actingUserId) ? "system" : $"user:{actingUserId}",
                Action = action,
                EntityType = nameof(WhatsAppTemplate),
                EntityId = entityId,
                BeforeJson = beforeJson,
                AfterJson = afterJson,
                AtUtc = DateTime.UtcNow
            });
        }

        // ── submission rate limit ───────────────────────────────────────────

        /// <summary>
        /// Sliding 1-hour window of submission timestamps stored in IMemoryCache. Lost on
        /// app restart, which is fine — Meta itself rate-limits per WABA so a brief
        /// post-restart laxity won't push us over their hard limit.
        /// </summary>
        private void EnforceSubmissionRateLimit()
        {
            var stamps = ReadSubmissionTimestamps()
                .Where(t => DateTime.UtcNow - t < SubmissionWindow)
                .ToList();

            if (stamps.Count >= SubmissionsPerHourLimit)
            {
                var oldest = stamps.Min();
                var retryAfter = SubmissionWindow - (DateTime.UtcNow - oldest);
                if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.FromMinutes(1);
                throw new TemplateRateLimitException(stamps.Count, SubmissionsPerHourLimit, retryAfter);
            }
        }

        private void RecordSubmissionTimestamp()
        {
            var stamps = ReadSubmissionTimestamps()
                .Where(t => DateTime.UtcNow - t < SubmissionWindow)
                .ToList();
            stamps.Add(DateTime.UtcNow);
            _cache.Set(SubmissionRateCacheKey, stamps, SubmissionWindow + TimeSpan.FromMinutes(5));
        }

        private List<DateTime> ReadSubmissionTimestamps() =>
            _cache.TryGetValue(SubmissionRateCacheKey, out List<DateTime>? stamps) && stamps is not null
                ? stamps
                : new List<DateTime>();

        private static class StructuralKey
        {
            public static readonly IEqualityComparer<(string, string)> Comparer =
                EqualityComparer<(string, string)>.Default;
        }
    }
}
