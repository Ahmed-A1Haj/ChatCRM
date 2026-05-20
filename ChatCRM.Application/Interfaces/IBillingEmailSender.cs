namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Billing-specific email surface. Kept separate from Identity's IEmailSender&lt;User&gt; so
    /// auth and billing concerns don't leak into each other.
    /// </summary>
    public interface IBillingEmailSender
    {
        /// <summary>Sent after a successful Stripe top-up. Best-effort; failures are logged but never bubble.</summary>
        Task SendTopUpReceiptAsync(string toEmail, string displayName, decimal amountUsd, decimal balanceAfterUsd, string sessionId, CancellationToken cancellationToken = default);
    }
}
