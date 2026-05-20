using System.Text.Json;
using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Invoices
{
    /// <summary>
    /// Workspace invoice issuance + read surface. Numbering is computed on issue, not on
    /// create, so Draft rows never burn a sequence number. Drafts can be regenerated freely;
    /// Issued rows are immutable.
    /// </summary>
    public sealed class InvoiceService : IInvoiceService
    {
        private const int DefaultWorkspaceId = 1;

        private readonly AppDbContext _db;
        private readonly IWalletService _wallet;
        private readonly IInvoicePdfRenderer _renderer;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(
            AppDbContext db,
            IWalletService wallet,
            IInvoicePdfRenderer renderer,
            ILogger<InvoiceService> logger)
        {
            _db = db;
            _wallet = wallet;
            _renderer = renderer;
            _logger = logger;
        }

        public async Task<List<InvoiceListItemDto>> ListAsync(CancellationToken ct = default)
        {
            return await _db.Invoices
                .AsNoTracking()
                .Where(i => i.WorkspaceId == DefaultWorkspaceId)
                .OrderByDescending(i => i.PeriodStartUtc)
                .ThenByDescending(i => i.CreatedAt)
                .Select(i => new InvoiceListItemDto
                {
                    Id = i.Id,
                    Number = i.Number,
                    PeriodStartUtc = i.PeriodStartUtc,
                    PeriodEndUtc = i.PeriodEndUtc,
                    Status = i.Status,
                    TotalChargedUsd = i.TotalChargedUsd,
                    TotalToppedUpUsd = i.TotalToppedUpUsd,
                    IssuedAt = i.IssuedAt,
                    IssuedByName = i.IssuedByUser != null
                        ? (string.IsNullOrWhiteSpace(i.IssuedByUser.FirstName)
                            ? i.IssuedByUser.Email
                            : (i.IssuedByUser.FirstName + " " + i.IssuedByUser.LastName).Trim())
                        : null
                })
                .ToListAsync(ct);
        }

        public async Task<InvoiceDetailDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var entity = await _db.Invoices
                .AsNoTracking()
                .Include(i => i.IssuedByUser)
                .FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == DefaultWorkspaceId, ct);
            return entity is null ? null : ToDetail(entity);
        }

        public async Task<InvoiceDetailDto> GenerateForMonthAsync(DateTime anyDayInPeriod, string actingUserId, CancellationToken ct = default)
        {
            var (start, end) = MonthBounds(anyDayInPeriod);

            // Snapshot totals from the wallet/billing-record aggregates for the period.
            var analytics = await _wallet.GetAnalyticsAsync(start, end, ct);

            var existing = await _db.Invoices
                .FirstOrDefaultAsync(i => i.WorkspaceId == DefaultWorkspaceId && i.PeriodStartUtc == start, ct);

            if (existing is { Status: InvoiceStatus.Issued })
            {
                _logger.LogInformation("[INVOICE] Regenerate skipped — {Number} already Issued.", existing.Number);
                return ToDetail(existing);
            }

            var nowUtc = DateTime.UtcNow;
            var categoryJson = JsonSerializer.Serialize(analytics.ByCategory);

            if (existing is null)
            {
                existing = new Invoice
                {
                    WorkspaceId = DefaultWorkspaceId,
                    Number = string.Empty, // assigned at issue time
                    PeriodStartUtc = start,
                    PeriodEndUtc = end,
                    Status = InvoiceStatus.Draft,
                    CreatedAt = nowUtc
                };
                _db.Invoices.Add(existing);
            }

            existing.TotalChargedUsd = analytics.TotalChargedUsd;
            existing.TotalMetaCostUsd = analytics.TotalMetaCostUsd;
            existing.TotalToppedUpUsd = analytics.TotalToppedUpUsd;
            existing.PaidMessageCount = analytics.PaidMessageCount;
            existing.FreeMessageCount = analytics.FreeMessageCount;
            existing.CategoryBreakdownJson = categoryJson;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = $"user:{actingUserId}",
                Action = "invoice.regenerated",
                EntityType = nameof(Invoice),
                EntityId = existing.Id == 0 ? "*" : existing.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new
                {
                    PeriodStartUtc = start,
                    existing.TotalChargedUsd,
                    existing.TotalMetaCostUsd,
                    existing.TotalToppedUpUsd
                }),
                AtUtc = nowUtc
            });

            await _db.SaveChangesAsync(ct);
            return ToDetail(existing);
        }

        public async Task<InvoiceDetailDto> IssueAsync(int id, string actingUserId, CancellationToken ct = default)
        {
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == DefaultWorkspaceId, ct)
                ?? throw new InvalidOperationException($"Invoice {id} not found.");

            if (invoice.Status == InvoiceStatus.Issued)
                return ToDetail(invoice); // idempotent
            if (invoice.Status == InvoiceStatus.Void)
                throw new InvalidOperationException("Voided invoices cannot be issued.");

            // Numbering is per-workspace per-month sequence. We do this inside the same SaveChanges
            // so the unique index on (WorkspaceId, Number) prevents two simultaneous issues from
            // colliding — if it does, the second caller gets a DbUpdateException and can retry.
            var monthSegment = invoice.PeriodStartUtc.ToString("yyyyMM");
            var prefix = $"INV-{monthSegment}-";

            var lastSeq = await _db.Invoices
                .Where(i => i.WorkspaceId == DefaultWorkspaceId
                         && i.Status == InvoiceStatus.Issued
                         && i.Number.StartsWith(prefix))
                .Select(i => i.Number)
                .ToListAsync(ct);

            var maxSeq = lastSeq
                .Select(n => int.TryParse(n.Substring(prefix.Length), out var s) ? s : 0)
                .DefaultIfEmpty(0)
                .Max();

            invoice.Number = $"{prefix}{(maxSeq + 1):D4}";
            invoice.Status = InvoiceStatus.Issued;
            invoice.IssuedAt = DateTime.UtcNow;
            invoice.IssuedByUserId = actingUserId;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = $"user:{actingUserId}",
                Action = "invoice.issued",
                EntityType = nameof(Invoice),
                EntityId = invoice.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new { invoice.Number, invoice.PeriodStartUtc, invoice.TotalChargedUsd }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[INVOICE] Issued {Number} for {Period}: charged {Amount}",
                invoice.Number, invoice.PeriodStartUtc.ToString("yyyy-MM"), invoice.TotalChargedUsd);
            return ToDetail(invoice);
        }

        public async Task<byte[]> RenderPdfAsync(int id, CancellationToken ct = default)
        {
            var detail = await GetAsync(id, ct)
                ?? throw new InvalidOperationException($"Invoice {id} not found.");
            return _renderer.Render(detail);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static (DateTime Start, DateTime End) MonthBounds(DateTime anyDay)
        {
            var d = anyDay.ToUniversalTime().Date;
            var start = new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            return (start, end);
        }

        private static InvoiceDetailDto ToDetail(Invoice i) => new()
        {
            Id = i.Id,
            Number = string.IsNullOrEmpty(i.Number) ? $"DRAFT-{i.PeriodStartUtc:yyyyMM}" : i.Number,
            PeriodStartUtc = i.PeriodStartUtc,
            PeriodEndUtc = i.PeriodEndUtc,
            Status = i.Status,
            TotalChargedUsd = i.TotalChargedUsd,
            TotalMetaCostUsd = i.TotalMetaCostUsd,
            TotalToppedUpUsd = i.TotalToppedUpUsd,
            PaidMessageCount = i.PaidMessageCount,
            FreeMessageCount = i.FreeMessageCount,
            ByCategory = string.IsNullOrEmpty(i.CategoryBreakdownJson)
                ? new()
                : JsonSerializer.Deserialize<List<BillingCategoryBreakdown>>(i.CategoryBreakdownJson) ?? new(),
            IssuedAt = i.IssuedAt,
            IssuedByName = i.IssuedByUser != null
                ? (string.IsNullOrWhiteSpace(i.IssuedByUser.FirstName)
                    ? i.IssuedByUser.Email
                    : (i.IssuedByUser.FirstName + " " + i.IssuedByUser.LastName).Trim())
                : null
        };
    }

    /// <summary>Pluggable interface so the QuestPDF impl can be swapped in tests.</summary>
    public interface IInvoicePdfRenderer
    {
        byte[] Render(InvoiceDetailDto invoice);
    }
}
