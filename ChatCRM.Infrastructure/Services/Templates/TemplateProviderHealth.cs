using ChatCRM.Application.Interfaces;

namespace ChatCRM.Infrastructure.Services.Templates
{
    /// <summary>
    /// Trivial volatile-field singleton. No locking — the worst race is a slightly-stale read
    /// of the last failure, which is acceptable because the next provider call will either
    /// confirm or reset the state.
    /// </summary>
    public sealed class TemplateProviderHealth : ITemplateProviderHealth
    {
        private DateTime? _lastFailureAtUtc;
        private string? _lastFailureMessage;

        public DateTime? LastAuthFailureAtUtc => _lastFailureAtUtc;
        public string? LastAuthFailureMessage => _lastFailureMessage;

        public void RecordAuthFailure(string message)
        {
            _lastFailureAtUtc = DateTime.UtcNow;
            _lastFailureMessage = message;
        }

        public void Reset()
        {
            _lastFailureAtUtc = null;
            _lastFailureMessage = null;
        }
    }
}
