using ChatCRM.Application.Billing.DTOs;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Read-only paged query over the <c>BillingAuditLog</c> table. Used by the audit-log UI
    /// gated under <c>PlatformAdmin</c> — surfaces every billing/wallet/template/invoice
    /// mutation that's been written across phases 0–11.
    /// </summary>
    public interface IAuditLogService
    {
        Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

        /// <summary>Distinct action strings — feeds the dropdown filter so users don't free-text the action.</summary>
        Task<List<string>> ListDistinctActionsAsync(CancellationToken cancellationToken = default);

        /// <summary>Distinct entity types — feeds the second dropdown filter.</summary>
        Task<List<string>> ListDistinctEntityTypesAsync(CancellationToken cancellationToken = default);
    }
}
