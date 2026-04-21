namespace ChatCRM.Infrastructure.Services
{
    public class EvolutionOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string InstanceName { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
