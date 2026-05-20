namespace ChatCRM.Application.Billing.Exceptions
{
    /// <summary>
    /// Thrown when an agent tries to send a free-form (Service-category) message to a contact
    /// who hasn't messaged in within the last 24 hours. After the window closes Meta requires a
    /// pre-approved template — the dashboard surfaces this so the agent can pick one instead of
    /// silently being charged for nothing.
    /// </summary>
    public sealed class ServiceWindowExpiredException : Exception
    {
        public DateTime? LastIncomingAtUtc { get; }

        public ServiceWindowExpiredException(DateTime? lastIncomingAtUtc)
            : base("The 24h customer-service window has closed. Send an approved template to re-open it.")
        {
            LastIncomingAtUtc = lastIncomingAtUtc;
        }
    }
}
