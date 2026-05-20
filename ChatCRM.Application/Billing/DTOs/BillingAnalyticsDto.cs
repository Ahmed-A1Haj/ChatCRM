using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Billing.DTOs
{
    /// <summary>
    /// Aggregate analytics for the Billing → Analytics page. Month-to-date totals plus a
    /// per-day series for the chart. Built from <c>WalletTransactions</c> + <c>MessageBillingRecords</c>
    /// with a single AppDbContext round trip per call.
    /// </summary>
    public sealed class BillingAnalyticsDto
    {
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }

        /// <summary>Sum of MessageCharge debits in the period (positive USD).</summary>
        public decimal TotalSpentUsd { get; set; }

        /// <summary>Sum of TopUp credits in the period (positive USD).</summary>
        public decimal TotalToppedUpUsd { get; set; }

        /// <summary>What we paid Meta (the floor; sum of <c>MetaCostUsd</c>).</summary>
        public decimal TotalMetaCostUsd { get; set; }

        /// <summary>What we charged the customer (sum of <c>ChargedUsd</c>).</summary>
        public decimal TotalChargedUsd { get; set; }

        /// <summary>Margin = TotalChargedUsd − TotalMetaCostUsd. Surfaces gross profit.</summary>
        public decimal MarginUsd => TotalChargedUsd - TotalMetaCostUsd;

        /// <summary>Number of paid (non-free) outgoing messages in the period.</summary>
        public int PaidMessageCount { get; set; }

        /// <summary>Number of free outgoing messages in the period (Service-window or free-entry-point).</summary>
        public int FreeMessageCount { get; set; }

        /// <summary>Per-day series — one row per day in the period, including zero days for chart continuity.</summary>
        public List<BillingDailyPoint> Daily { get; set; } = new();

        /// <summary>Per-category breakdown for a stacked-pie / segmented bar.</summary>
        public List<BillingCategoryBreakdown> ByCategory { get; set; } = new();
    }

    public sealed class BillingDailyPoint
    {
        public DateTime DayUtc { get; set; }
        public decimal SpentUsd { get; set; }
        public decimal ToppedUpUsd { get; set; }
        public int PaidMessages { get; set; }
    }

    public sealed class BillingCategoryBreakdown
    {
        public MetaMessageCategory Category { get; set; }
        public int Count { get; set; }
        public decimal ChargedUsd { get; set; }
    }

    /// <summary>
    /// Filterable transactions log. Mirrors what <see cref="BillingOverviewDto"/> shows on the
    /// landing card but with date range + kind/status filters and pagination cursor.
    /// </summary>
    public sealed class WalletTransactionLogQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public WalletTransactionKind? Kind { get; set; }
        public WalletTransactionStatus? Status { get; set; }
        /// <summary>0-indexed page; rows per page is clamped at 100.</summary>
        public int Page { get; set; } = 0;
        public int PageSize { get; set; } = 50;
    }

    public sealed class WalletTransactionLogPage
    {
        public List<WalletTransactionDto> Rows { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasMore => (Page + 1) * PageSize < TotalCount;
    }
}
