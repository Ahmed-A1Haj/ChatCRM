using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Billing.DTOs
{
    /// <summary>
    /// Platform-level KPIs surfaced on the PlatformAdmin dashboard. v1 has a single workspace
    /// so "top spenders" is degenerate — the breakdown is by message category rather than by
    /// workspace, ready to widen when multi-tenant arrives.
    /// </summary>
    public sealed class PlatformOverviewDto
    {
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }

        /// <summary>Across all workspaces and the period.</summary>
        public decimal TotalRevenueUsd { get; set; }
        public decimal TotalMetaCostUsd { get; set; }
        public decimal MarginUsd => TotalRevenueUsd - TotalMetaCostUsd;
        public decimal MarginPercent => TotalRevenueUsd > 0m ? Math.Round(MarginUsd / TotalRevenueUsd * 100m, 2) : 0m;

        public int TotalPaidMessageCount { get; set; }
        public int TotalFreeMessageCount { get; set; }
        public int ActiveWorkspaceCount { get; set; }

        public List<TopSpenderDto> TopSpenders { get; set; } = new();
        public List<BillingCategoryBreakdown> ByCategory { get; set; } = new();
    }

    public sealed class TopSpenderDto
    {
        public int WorkspaceId { get; set; }
        public decimal SpentUsd { get; set; }
        public int MessageCount { get; set; }
        public decimal CurrentBalanceUsd { get; set; }
    }
}
