namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Idempotency log for Stripe webhook deliveries. Stripe retries failed deliveries (and
    /// occasionally double-delivers successful ones); inserting a row keyed by the event id
    /// the first time we process it lets us short-circuit second-and-subsequent deliveries.
    ///
    /// Cleanup: a 90-day retention job is added in phase 11 (alongside Hangfire). For v1
    /// the table just grows — at typical webhook volume that's fine.
    /// </summary>
    public class ProcessedStripeEvent
    {
        /// <summary>Stripe's <c>evt_xxx</c> id. Primary key — duplicates are blocked at the DB.</summary>
        public string EventId { get; set; } = string.Empty;

        public string EventType { get; set; } = string.Empty;
        public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
