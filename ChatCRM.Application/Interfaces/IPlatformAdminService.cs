using ChatCRM.Application.Billing.DTOs;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Cross-workspace read surface for the platform admin (the "us" perspective). Gated by
    /// the <c>PlatformAdmin</c> permission claim — separates "workspace owner doing billing"
    /// from "platform operator doing finance reporting".
    /// </summary>
    public interface IPlatformAdminService
    {
        /// <summary>Aggregate KPIs + top spenders + category breakdown for the chosen window.</summary>
        Task<PlatformOverviewDto> GetOverviewAsync(
            DateTime fromUtc, DateTime toUtc, int topN = 10,
            CancellationToken cancellationToken = default);
    }
}
