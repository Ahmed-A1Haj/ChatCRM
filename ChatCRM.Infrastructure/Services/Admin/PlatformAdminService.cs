using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChatCRM.Infrastructure.Services.Admin
{
    /// <summary>
    /// Cross-workspace aggregate reader. v1 has a single workspace so the "top spenders" list is
    /// always one row; the shape is multi-tenant-ready so when we add per-workspace billing we
    /// don't need to refactor the dashboard.
    /// </summary>
    public sealed class PlatformAdminService : IPlatformAdminService
    {
        private readonly AppDbContext _db;

        public PlatformAdminService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<PlatformOverviewDto> GetOverviewAsync(DateTime fromUtc, DateTime toUtc, int topN = 10, CancellationToken ct = default)
        {
            // MessageBillingRecords are the source of truth for "what we charged & paid Meta".
            // WalletTransactions are the cash flow — they don't help with margin since refunds
            // muddy the picture.
            var records = await _db.MessageBillingRecords
                .AsNoTracking()
                .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc)
                .Select(r => new { r.Category, r.MetaCostUsd, r.ChargedUsd, MessageId = r.MessageId })
                .ToListAsync(ct);

            var totalRevenue = records.Sum(r => r.ChargedUsd);
            var totalMeta = records.Sum(r => r.MetaCostUsd);
            var paidCount = records.Count(r => r.ChargedUsd > 0m);
            var freeCount = records.Count - paidCount;

            var byCategory = records
                .GroupBy(r => r.Category)
                .Select(g => new BillingCategoryBreakdown
                {
                    Category = g.Key,
                    Count = g.Count(),
                    ChargedUsd = g.Sum(x => x.ChargedUsd)
                })
                .OrderByDescending(c => c.ChargedUsd)
                .ToList();

            // Active workspaces = those with at least one wallet that has activity in the period.
            // v1: pull all wallets and their balances; bind ChargedUsd to the wallet's workspace.
            var wallets = await _db.Wallets
                .AsNoTracking()
                .Select(w => new { w.WorkspaceId, w.BalanceUsd })
                .ToListAsync(ct);

            // Per-workspace spend in the period — we don't tag MessageBillingRecord with a
            // workspace yet (single-workspace v1), so attribute everything to WorkspaceId=1.
            var spendByWorkspace = wallets
                .Select(w => new TopSpenderDto
                {
                    WorkspaceId = w.WorkspaceId,
                    SpentUsd = totalRevenue, // TODO multi-tenant: filter by tagged workspace
                    MessageCount = paidCount,
                    CurrentBalanceUsd = w.BalanceUsd
                })
                .OrderByDescending(s => s.SpentUsd)
                .Take(topN)
                .ToList();

            return new PlatformOverviewDto
            {
                PeriodStartUtc = fromUtc,
                PeriodEndUtc = toUtc,
                TotalRevenueUsd = totalRevenue,
                TotalMetaCostUsd = totalMeta,
                TotalPaidMessageCount = paidCount,
                TotalFreeMessageCount = freeCount,
                ActiveWorkspaceCount = wallets.Count,
                TopSpenders = spendByWorkspace,
                ByCategory = byCategory
            };
        }
    }
}
