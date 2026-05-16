namespace ChatCRM.Infrastructure.Services.Ai
{
    /// <summary>
    /// Bound from the <c>Ai</c> section of configuration. See <c>appsettings.json</c> for
    /// defaults; per-environment overrides go in <c>appsettings.{Environment}.json</c> or
    /// <c>Ai__*</c> environment variables.
    /// </summary>
    public sealed class AiOptions
    {
        /// <summary>StackExchange.Redis connection string. e.g. <c>localhost:6379</c> in dev.</summary>
        public string RedisUrl { get; set; } = "localhost:6379";

        public string InboundStream { get; set; } = "stream:inbound";
        public string OutboundStream { get; set; } = "stream:outbound";

        public string ConsumerGroup { get; set; } = "chatcrm-outbound-workers";
        public string ConsumerName { get; set; } = "web-1";

        /// <summary>Used as the <c>tenant_id</c> field on every outbound AI message. Singleton workspace today.</summary>
        public string TenantId { get; set; } = "workspace-1";

        /// <summary>How often the outbox publisher polls the DB.</summary>
        public int PublisherPollMilliseconds { get; set; } = 1000;

        /// <summary>Batch size for each publisher tick.</summary>
        public int PublisherBatchSize { get; set; } = 50;
    }
}
