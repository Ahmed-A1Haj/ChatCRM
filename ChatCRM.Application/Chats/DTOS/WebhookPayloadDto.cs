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

        [JsonPropertyName("messageType")]
        public string? MessageType { get; set; }

        [JsonPropertyName("messageTimestamp")]
        public long MessageTimestamp { get; set; }

        [JsonPropertyName("pushName")]
        public string? PushName { get; set; }

        /// <summary>messages.update events carry the original message id under "keyId".</summary>
        [JsonPropertyName("keyId")]
        public string? KeyId { get; set; }

        /// <summary>messages.delete events carry the deleted message id under "id".</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>For messages.delete this is "DELETED"; for status updates it's "READ"/"DELIVERED" etc.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }
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

        [JsonPropertyName("imageMessage")]
        public WebhookMediaMessage? ImageMessage { get; set; }

        [JsonPropertyName("videoMessage")]
        public WebhookMediaMessage? VideoMessage { get; set; }

        [JsonPropertyName("audioMessage")]
        public WebhookMediaMessage? AudioMessage { get; set; }

        [JsonPropertyName("documentMessage")]
        public WebhookMediaMessage? DocumentMessage { get; set; }

        [JsonPropertyName("documentWithCaptionMessage")]
        public WebhookDocumentWithCaption? DocumentWithCaptionMessage { get; set; }

        [JsonPropertyName("stickerMessage")]
        public WebhookMediaMessage? StickerMessage { get; set; }

        /// <summary>
        /// Baileys uses protocolMessage to deliver edit (type=14) and revoke (type=0)
        /// notifications inline with messages.upsert, with key.id pointing at the
        /// original message and editedMessage carrying the new body when applicable.
        /// </summary>
        [JsonPropertyName("protocolMessage")]
        public WebhookProtocolMessage? ProtocolMessage { get; set; }

        /// <summary>Edits sometimes arrive wrapped in editedMessage.message.protocolMessage.</summary>
        [JsonPropertyName("editedMessage")]
        public WebhookEditedMessage? EditedMessage { get; set; }
    }

    public class WebhookProtocolMessage
    {
        [JsonPropertyName("key")]
        public WebhookMessageKey? Key { get; set; }

        /// <summary>Baileys WAMessageStubType. 0 = REVOKE (delete for everyone), 14 = MESSAGE_EDIT.</summary>
        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("editedMessage")]
        public WebhookMessageContent? EditedMessage { get; set; }
    }

    public class WebhookEditedMessage
    {
        /// <summary>Recursively a WebhookMessageContent so we can dig through any wrapping
        /// (message.editedMessage.message.conversation as well as the deeper
        /// message.editedMessage.message.protocolMessage.editedMessage.conversation form).</summary>
        [JsonPropertyName("message")]
        public WebhookMessageContent? Message { get; set; }
    }

    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class WebhookMediaMessage
    {
        [JsonPropertyName("mimetype")]
        public string? Mimetype { get; set; }

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }
    }

    public class WebhookDocumentWithCaption
    {
        [JsonPropertyName("message")]
        public WebhookDocumentInner? Message { get; set; }
    }

    public class WebhookDocumentInner
    {
        [JsonPropertyName("documentMessage")]
        public WebhookMediaMessage? DocumentMessage { get; set; }
    }
}
