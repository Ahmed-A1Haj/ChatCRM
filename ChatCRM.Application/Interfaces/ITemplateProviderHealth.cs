namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// In-process tracker for Meta provider health. Lives as a singleton so a 401 detected by
    /// the hosted poller is surfaced to subsequent UI requests without each request having to
    /// reach Meta first. Resets on app restart by design — the next call will either succeed
    /// (token rotated) or re-flag (still bad).
    /// </summary>
    public interface ITemplateProviderHealth
    {
        DateTime? LastAuthFailureAtUtc { get; }
        string? LastAuthFailureMessage { get; }

        void RecordAuthFailure(string message);
        void Reset();
    }
}
