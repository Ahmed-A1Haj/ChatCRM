using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Per-message billing pipeline. Sits between the chat send action and the Evolution API call:
    /// quote → enforce service window → reserve funds → caller invokes Meta → commit on success,
    /// release on failure. Personal (Baileys) instances bypass entirely — those are Web-scrape
    /// accounts that don't bill us.
    /// </summary>
    public interface IBillingGate
    {
        /// <summary>
        /// Quote the cost, validate the service window, and reserve the funds from the wallet.
        /// Returns null when the instance bypasses billing (Personal/Baileys); returns a populated
        /// reservation otherwise. Throws <see cref="ChatCRM.Application.Billing.Exceptions.InsufficientBalanceException"/>
        /// or <see cref="ChatCRM.Application.Billing.Exceptions.ServiceWindowExpiredException"/>
        /// before any wallet movement happens — caller can surface the failure cleanly.
        /// </summary>
        Task<BillingReservation?> ReserveAsync(
            Conversation conversation,
            string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reserve funds for a template send. Skips the service-window check (templates are
        /// the *answer* to a closed window) and passes the explicit non-Service category to
        /// the pricing engine. Returns null for Personal/Baileys instances. Throws
        /// <see cref="ChatCRM.Application.Billing.Exceptions.InsufficientBalanceException"/>.
        /// </summary>
        Task<BillingReservation?> ReserveForTemplateAsync(
            Conversation conversation,
            MetaMessageCategory category,
            string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persist the reservation against the now-saved Message: write the
        /// <see cref="MessageBillingRecord"/>, finalise the WalletTransaction (Pending → Succeeded
        /// for paid sends, void it for free sends), and update the wallet balance once. Idempotent
        /// on re-call (re-call paths shouldn't exist, but the unique index on MessageId protects us).
        /// </summary>
        Task CommitAsync(
            BillingReservation reservation,
            int messageId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Roll back a reservation when the Meta send failed. The Pending WalletTransaction is
        /// marked Failed and no record is written. No wallet movement to undo because Reserve
        /// never debited the wallet — Pending is a soft hold tracked by GetEffectiveBalanceAsync.
        /// </summary>
        Task ReleaseAsync(
            BillingReservation reservation,
            string reason,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Soft-hold descriptor returned from Reserve. Carries the quote (so Commit can write the
    /// audit row without re-pricing) and the Pending transaction id (so Commit/Release can find
    /// the row by id without another reference lookup).
    /// </summary>
    public sealed record BillingReservation(
        long? PendingTransactionId,
        PricingQuote Quote,
        int ConversationId,
        string ActingUserId);
}
