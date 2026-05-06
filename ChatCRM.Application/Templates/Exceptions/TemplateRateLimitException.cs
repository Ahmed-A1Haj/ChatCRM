namespace ChatCRM.Application.Templates.Exceptions
{
    /// <summary>
    /// Thrown when a workspace exceeds the local submission rate limit (default 10/hour).
    /// Catches the user *before* they hit Meta's opaque 429 — we'd rather a friendly
    /// "you've hit Meta's submission limit, try again in N minutes" than a Graph API error.
    /// </summary>
    public sealed class TemplateRateLimitException : Exception
    {
        public int SubmissionsInWindow { get; }
        public int LimitPerHour { get; }
        public TimeSpan RetryAfter { get; }

        public TemplateRateLimitException(int submissionsInWindow, int limitPerHour, TimeSpan retryAfter)
            : base($"Template submission rate limit reached ({submissionsInWindow}/{limitPerHour} per hour). Retry in {retryAfter.TotalMinutes:0} minute(s).")
        {
            SubmissionsInWindow = submissionsInWindow;
            LimitPerHour = limitPerHour;
            RetryAfter = retryAfter;
        }
    }
}
