using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatCRM.Infrastructure.Services.Audit
{
    public sealed class AuditLogService : IAuditLogService
    {
        private readonly AppDbContext _db;

        public AuditLogService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            var q = _db.BillingAuditLogs.AsNoTracking().AsQueryable();

            if (query.FromUtc.HasValue) q = q.Where(x => x.AtUtc >= query.FromUtc.Value);
            if (query.ToUtc.HasValue)   q = q.Where(x => x.AtUtc <  query.ToUtc.Value);
            if (!string.IsNullOrWhiteSpace(query.Actor))
                q = q.Where(x => x.Actor.Contains(query.Actor));
            if (!string.IsNullOrWhiteSpace(query.Action))
                q = q.Where(x => x.Action == query.Action);
            if (!string.IsNullOrWhiteSpace(query.EntityType))
                q = q.Where(x => x.EntityType == query.EntityType);
            if (!string.IsNullOrWhiteSpace(query.EntityId))
                q = q.Where(x => x.EntityId == query.EntityId);

            var total = await q.CountAsync(ct);
            var page = Math.Max(0, query.Page);
            var size = Math.Clamp(query.PageSize, 10, 200);

            var rows = await q.OrderByDescending(x => x.AtUtc)
                .Skip(page * size).Take(size)
                .Select(x => new AuditLogRowDto
                {
                    Id = x.Id,
                    AtUtc = x.AtUtc,
                    Actor = x.Actor,
                    Action = x.Action,
                    EntityType = x.EntityType,
                    EntityId = x.EntityId,
                    BeforeJson = x.BeforeJson,
                    AfterJson = x.AfterJson
                })
                .ToListAsync(ct);

            return new AuditLogPage { Rows = rows, Page = page, PageSize = size, TotalCount = total };
        }

        public async Task<List<string>> ListDistinctActionsAsync(CancellationToken ct = default) =>
            await _db.BillingAuditLogs.AsNoTracking()
                .Select(x => x.Action).Distinct().OrderBy(s => s).ToListAsync(ct);

        public async Task<List<string>> ListDistinctEntityTypesAsync(CancellationToken ct = default) =>
            await _db.BillingAuditLogs.AsNoTracking()
                .Select(x => x.EntityType).Distinct().OrderBy(s => s).ToListAsync(ct);
    }
}
