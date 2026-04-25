namespace ChatCRM.Infrastructure.Services
{
    public class EvolutionOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string InstanceName { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;

        /// <summary>
        /// Public base URL where Evolution API can POST webhooks (e.g. ngrok URL in dev, or your deployed app URL).
        /// Leave empty to skip auto-registering webhooks for new instances.
        /// The full webhook URL will be {WebhookPublicBaseUrl}/api/evolution/webhook.
        /// </summary>
        public string? WebhookPublicBaseUrl { get; set; }
    }
}
