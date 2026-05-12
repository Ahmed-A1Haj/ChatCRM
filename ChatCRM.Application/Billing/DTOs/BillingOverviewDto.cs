using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Billing.DTOs
{
    /// <summary>Read model that powers the Billing overview page.</summary>
    public sealed class BillingOverviewDto
    {
        public required decimal BalanceUsd { get; init; }
        public required decimal LowBalanceThresholdUsd { get; init; }
        public required string DisplayCurrency { get; init; }
        public required BalanceHealth Health { get; init; }

        /// <summary>Markdown text from BillingSettings.</summary>
        public required string RefundPolicy { get; init; }

        public required decimal MarkupPercentage { get; init; }

        /// <summary>Last 10 wallet movements, newest-first.</summary>
        public required IReadOnlyList<WalletTransactionDto> RecentTransactions { get; init; }
    }

    public enum BalanceHealth { Healthy, Low, Empty, Negative }

    public sealed class WalletTransactionDto
    {
        public required long Id { get; init; }
        public required WalletTransactionKind Kind { get; init; }
        public required WalletTransactionStatus Status { get; init; }
        public required decimal AmountUsd { get; init; }
        public required decimal BalanceAfterUsd { get; init; }
        public string? Reference { get; init; }
        public string? Reason { get; init; }
        public string? InitiatedByName { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    /// <summary>POST body for the manual balance adjustment form (admin-only).</summary>
    public sealed class AdjustBalanceDto
    {
        /// <summary>Always positive — sign comes from <see cref="IsCredit"/>.</summary>
        public decimal AmountUsd { get; set; }

        /// <summary>True for ManualCredit, false for ManualDebit.</summary>
        public bool IsCredit { get; set; }

        /// <summary>Required free-text justification — appears in the audit log + ledger row.</summary>
        public string Reason { get; set; } = string.Empty;
    }
}
