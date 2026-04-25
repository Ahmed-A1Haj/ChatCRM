using System.Text.Json.Serialization;

namespace ChatCRM.Application.Chats.DTOs
{
    public class WebhookPayloadDto
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

        [JsonPropertyName("instance")]
        public string? Instance { get; set; }

        [JsonPropertyName("data")]
        public WebhookMessageData? Data { get; set; }
    }

    public class WebhookMessageData
    {
        [JsonPropertyName("key")]
        public WebhookMessageKey? Key { get; set; }

        [JsonPropertyName("message")]
        public WebhookMessageContent? Message { get; set; }

        [JsonPropertyName("messageTimestamp")]
        public long MessageTimestamp { get; set; }

        [JsonPropertyName("pushName")]
        public string? PushName { get; set; }
    }

    public class WebhookMessageKey
    {
        [JsonPropertyName("remoteJid")]
        public string RemoteJid { get; set; } = string.Empty;

        [JsonPropertyName("fromMe")]
        public bool FromMe { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    public class WebhookMessageContent
    {
        [JsonPropertyName("conversation")]
        public string? Conversation { get; set; }

        [JsonPropertyName("extendedTextMessage")]
        public ExtendedTextMessage? ExtendedTextMessage { get; set; }
    }

    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
