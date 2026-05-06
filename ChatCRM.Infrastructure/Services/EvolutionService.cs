using System.Text;
using System.Text.Json;
using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public class EvolutionService : IEvolutionService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHubContext<ChatHub> _hub;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EvolutionService> _logger;

        public EvolutionService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IHubContext<ChatHub> hub,
            IWebHostEnvironment env,
            ILogger<EvolutionService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _hub = hub;
            _env = env;
            _logger = logger;
        }

        public Task<EvolutionSendResult> SendMessageAsync(string instanceName, string phone, string message, CancellationToken cancellationToken = default)
            => PostSendAsync($"/message/sendText/{instanceName}", new { number = phone, text = message }, instanceName, phone, cancellationToken);

        public Task<EvolutionSendResult> SendMediaAsync(
            string instanceName,
            string phone,
            string mediaType,
            byte[] data,
            string? mimeType,
            string? fileName,
            string? caption,
            CancellationToken cancellationToken = default)
        {
            var payload = new
            {
                number = phone,
                mediatype = mediaType,
                mimetype = mimeType,
                media = Convert.ToBase64String(data),
                fileName = fileName ?? $"file{Path.GetRandomFileName()}",
                caption = caption ?? string.Empty
            };
            return PostSendAsync($"/message/sendMedia/{instanceName}", payload, instanceName, phone, cancellationToken);
        }

        public Task<EvolutionSendResult> SendVoiceNoteAsync(
            string instanceName,
            string phone,
            byte[] data,
            CancellationToken cancellationToken = default)
        {
            var payload = new
            {
                number = phone,
                audio = Convert.ToBase64String(data),
                encoding = true
            };
            return PostSendAsync($"/message/sendWhatsAppAudio/{instanceName}", payload, instanceName, phone, cancellationToken);
        }

        public async Task<bool> EditMessageAsync(
            string instanceName,
            string remoteJid,
            string externalMessageId,
            string newText,
            CancellationToken cancellationToken = default)
        {
            // Evolution's `number` field expects the bare phone, not the full JID.
            var bareNumber = remoteJid.Split('@')[0];

            var payload = new
            {
                number = bareNumber,
                key = new { id = externalMessageId, remoteJid, fromMe = true },
                text = newText
            };
            var json = JsonSerializer.Serialize(payload);

            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"/chat/updateMessage/{instanceName}", content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "Evolution updateMessage failed {Status} for {Instance}/{Id}.\n  Request body: {Req}\n  Response body: {Resp}",
                        response.StatusCode, instanceName, externalMessageId, json, err);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Edit threw for {Id} via {Instance}. Request body: {Req}", externalMessageId, instanceName, json);
                return false;
            }
        }

        public async Task<bool> DeleteMessageAsync(
            string instanceName,
            string remoteJid,
            string externalMessageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var payload = new { id = externalMessageId, remoteJid, fromMe = true };
                var req = new HttpRequestMessage(HttpMethod.Delete, $"/chat/deleteMessageForEveryone/{instanceName}")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                var response = await client.SendAsync(req, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution delete failed {Status} for {Instance}/{Id}: {Body}",
                        response.StatusCode, instanceName, externalMessageId, err);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete failed for {Id} via {Instance}", externalMessageId, instanceName);
                return false;
            }
        }

        /// <summary>
        /// Shared POST helper used by SendMessage / SendMedia / SendVoice. Posts the payload,
        /// parses Evolution's response, and surfaces (success, externalId, remoteJid) so the
        /// caller can persist the IDs needed for later edit/delete.
        /// </summary>
        private async Task<EvolutionSendResult> PostSendAsync(
            string path,
            object payload,
            string instanceName,
            string phone,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                _logger.LogError("Outbound call to {Path} skipped — empty instance name.", path);
                return new EvolutionSendResult(false, null, null);
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(path, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution {Path} returned {Status}: {Body}", path, response.StatusCode, err);
                    return new EvolutionSendResult(false, null, null);
                }

                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                string? externalId = null;
                string? remoteJid = null;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("key", out var key))
                    {
                        if (key.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            externalId = idEl.GetString();
                        if (key.TryGetProperty("remoteJid", out var jidEl) && jidEl.ValueKind == JsonValueKind.String)
                            remoteJid = jidEl.GetString();
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Could not parse Evolution response for {Path}: {Body}", path, raw);
                }

                return new EvolutionSendResult(true, externalId, remoteJid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbound {Path} threw for {Instance}/{Phone}", path, instanceName, phone);
                return new EvolutionSendResult(false, null, null);
            }
        }

        public async Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default)
        {
            if (payload.Data is null)
                return;

            if (string.IsNullOrWhiteSpace(payload.Instance))
            {
                _logger.LogWarning("Webhook missing instance name — cannot route.");
                return;
            }

            // Find the ChatCRM instance that owns this WhatsApp number.
            var instance = await _db.WhatsAppInstances
                .FirstOrDefaultAsync(i => i.InstanceName == payload.Instance, cancellationToken);

            if (instance is null)
            {
                _logger.LogWarning("Webhook for unknown instance {Instance} — dropping.", payload.Instance);
                return;
            }

            // ── messages.delete ─────────────────────────────────────────────────────
            // Payload shape: { event:"messages.delete", data:{ id:"...", status:"DELETED", ... } }
            if (string.Equals(payload.Event, "messages.delete", StringComparison.OrdinalIgnoreCase))
            {
                var deletedId = payload.Data.Id ?? payload.Data.Key?.Id;
                if (!string.IsNullOrEmpty(deletedId))
                    await MarkMessageDeletedAsync(deletedId, instance.Id, cancellationToken);
                return;
            }

            // ── messages.update ─────────────────────────────────────────────────────
            // Payload shape: { event:"messages.update", data:{ keyId:"...", message:{ editedMessage:{ message:{ conversation:"..." } } } } }
            if (string.Equals(payload.Event, "messages.update", StringComparison.OrdinalIgnoreCase))
            {
                var targetId = payload.Data.KeyId ?? payload.Data.Key?.Id;
                if (string.IsNullOrEmpty(targetId)) return;

                var newBody = ExtractEditedBody(payload.Data.Message);
                if (string.IsNullOrEmpty(newBody))
                {
                    // status-only update (READ / DELIVERED / etc.) — nothing to render.
                    return;
                }

                await ApplyMessageEditAsync(targetId, newBody, instance.Id, cancellationToken);
                return;
            }

            // ── messages.upsert (default) — needs key + message ─────────────────────
            if (payload.Data.Key is null || payload.Data.Message is null)
                return;

            // fromMe=true means the message originated on the linked WhatsApp account itself
            // (the agent typing on Ahmed's phone, or this CRM having just sent it). We still
            // want to persist it so the dashboard reflects the conversation state — the dedupe
            // check below handles the case where the CRM already inserted the row.
            var isOutgoing = payload.Data.Key.FromMe;

            // Some Baileys versions deliver edits/revokes inline with upsert as a protocolMessage.
            var protocolEnvelope = payload.Data.Message.ProtocolMessage
                                ?? payload.Data.Message.EditedMessage?.Message?.ProtocolMessage;
            if (protocolEnvelope is not null)
            {
                await HandleProtocolMessageAsync(protocolEnvelope, instance.Id, cancellationToken);
                return;
            }

            var externalId = payload.Data.Key.Id;

            // Deduplicate
            var alreadyProcessed = await _db.Messages
                .AnyAsync(m => m.ExternalId == externalId, cancellationToken);

            if (alreadyProcessed)
                return;

            var rawJid = payload.Data.Key.RemoteJid;
            var phone = rawJid.Split('@')[0];

            var (kind, body, mime, fileName) = ResolveMessagePayload(payload.Data);

            // If we couldn't classify it as anything we render, log what Evolution sent
            // so we can extend ResolveMessagePayload (reactions, polls, contacts, etc.).
            if (kind == MessageKind.Text && string.IsNullOrWhiteSpace(body))
            {
                _logger.LogInformation(
                    "Unhandled inbound payload — event={Event} messageType={MessageType} externalId={Id}",
                    "messages.upsert", payload.Data.MessageType, externalId);
                return;
            }

            var sentAt = payload.Data.MessageTimestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(payload.Data.MessageTimestamp).UtcDateTime
                : DateTime.UtcNow;

            // Upsert contact (contacts are global — a person is the same across all lines).
            var contact = await _db.WhatsAppContacts
                .FirstOrDefaultAsync(c => c.PhoneNumber == phone, cancellationToken);

            if (contact is null)
            {
                contact = new WhatsAppContact
                {
                    PhoneNumber = phone,
                    RemoteJid = rawJid,
                    DisplayName = payload.Data.PushName,
                    Country = PhoneCountryDetector.Detect(phone),
                    CreatedAt = DateTime.UtcNow
                };
                _db.WhatsAppContacts.Add(contact);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (contact.DisplayName is null && payload.Data.PushName is not null)
                    contact.DisplayName = payload.Data.PushName;
                // Always refresh RemoteJid — the contact may have switched between @s.whatsapp.net
                // and @lid forms over time, and we need the most recent one for edit/delete.
                if (!string.IsNullOrEmpty(rawJid) && contact.RemoteJid != rawJid)
                    contact.RemoteJid = rawJid;
            }

            // Hard-block check — if the user has blocked this contact, INCOMING messages get
            // dropped silently. Outgoing messages (the agent typing on the linked phone) still
            // get recorded so the timeline stays accurate.
            if (contact.IsBlocked && !isOutgoing)
            {
                _logger.LogInformation("Dropped inbound message from blocked contact {Phone} (id {ContactId})",
                    phone, contact.Id);
                return;
            }

            // One conversation per (contact, instance).
            var conversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ContactId == contact.Id && c.WhatsAppInstanceId == instance.Id, cancellationToken);

            if (conversation is null)
            {
                conversation = new Conversation
                {
                    ContactId = contact.Id,
                    WhatsAppInstanceId = instance.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Conversations.Add(conversation);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var message = new Message
            {
                ConversationId = conversation.Id,
                Body = body ?? string.Empty,
                Direction = isOutgoing ? MessageDirection.Outgoing : MessageDirection.Incoming,
                Status = MessageStatus.Sent,
                ExternalId = externalId,
                SentAt = sentAt,
                Kind = kind,
                MediaMimeType = mime,
                MediaFileName = fileName
            };

            _db.Messages.Add(message);
            conversation.LastMessageAt = sentAt;
            // Only inbound messages count toward unread — outbound (fromMe) doesn't.
            if (!isOutgoing) conversation.UnreadCount += 1;

            await _db.SaveChangesAsync(cancellationToken);

            // Fetch + persist the actual media file so it's renderable in the UI.
            if (kind != MessageKind.Text)
            {
                var mediaUrl = await DownloadAndStoreMediaAsync(payload.Instance!, externalId, message.Id, mime, cancellationToken);
                if (mediaUrl is not null)
                {
                    message.MediaUrl = mediaUrl;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            var instanceUnread = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == instance.Id && !c.IsArchived)
                .SumAsync(c => c.UnreadCount, cancellationToken);

            var instanceChatCount = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == instance.Id && !c.IsArchived)
                .CountAsync(cancellationToken);

            // Broadcast to the SignalR group for this specific instance.
            await _hub.Clients.Group(ChatHub.InstanceGroupName(instance.Id))
                .SendAsync("ReceiveMessage", new
                {
                    instanceId = instance.Id,
                    instanceUnread,
                    instanceChatCount,
                    conversationId = conversation.Id,
                    contactPhone = contact.PhoneNumber,
                    contactName = contact.DisplayName,
                    message = new
                    {
                        id = message.Id,
                        body = message.Body,
                        direction = (int)message.Direction,
                        sentAt = message.SentAt,
                        kind = (int)message.Kind,
                        mediaUrl = message.MediaUrl,
                        mediaMimeType = message.MediaMimeType,
                        mediaFileName = message.MediaFileName
                    },
                    unreadCount = conversation.UnreadCount
                }, cancellationToken);
        }

        /// <summary>
        /// Applies a Baileys protocolMessage (edit or revoke) to the previously stored
        /// message identified by protocol.Key.Id. Used by the messages.upsert path when
        /// some Baileys builds deliver edits/revokes inline rather than as messages.update.
        /// </summary>
        private async Task HandleProtocolMessageAsync(
            WebhookProtocolMessage protocol,
            int instanceId,
            CancellationToken cancellationToken)
        {
            var targetExternalId = protocol.Key?.Id;
            _logger.LogInformation(
                "protocolMessage received — type={Type} targetId={TargetId} hasEditedPayload={HasEdit}",
                protocol.Type, targetExternalId, protocol.EditedMessage is not null);

            if (string.IsNullOrEmpty(targetExternalId))
                return;

            var hasEditPayload = protocol.EditedMessage is not null;
            var isRevoke = protocol.Type == 0 && !hasEditPayload;

            if (isRevoke)
            {
                await MarkMessageDeletedAsync(targetExternalId, instanceId, cancellationToken);
                return;
            }

            if (hasEditPayload)
            {
                var newBody = ExtractEditedBody(protocol.EditedMessage);
                if (!string.IsNullOrEmpty(newBody))
                    await ApplyMessageEditAsync(targetExternalId, newBody, instanceId, cancellationToken);
            }
        }

        private async Task MarkMessageDeletedAsync(string externalId, int instanceId, CancellationToken cancellationToken)
        {
            var original = await _db.Messages
                .FirstOrDefaultAsync(m => m.ExternalId == externalId, cancellationToken);
            if (original is null)
            {
                _logger.LogInformation("Delete for unknown external message {Id} — ignored.", externalId);
                return;
            }

            if (original.IsDeleted) return;

            original.IsDeleted = true;
            original.Body = string.Empty;
            original.MediaUrl = null;
            original.MediaMimeType = null;
            original.MediaFileName = null;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(instanceId))
                .SendAsync("MessageDeleted", new
                {
                    instanceId,
                    conversationId = original.ConversationId,
                    messageId = original.Id
                }, cancellationToken);
        }

        private async Task ApplyMessageEditAsync(string externalId, string newBody, int instanceId, CancellationToken cancellationToken)
        {
            var original = await _db.Messages
                .FirstOrDefaultAsync(m => m.ExternalId == externalId, cancellationToken);
            if (original is null)
            {
                _logger.LogInformation("Edit for unknown external message {Id} — ignored.", externalId);
                return;
            }

            original.Body = newBody;
            original.EditedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(instanceId))
                .SendAsync("MessageEdited", new
                {
                    instanceId,
                    conversationId = original.ConversationId,
                    messageId = original.Id,
                    body = original.Body,
                    editedAt = original.EditedAt
                }, cancellationToken);
        }

        /// <summary>
        /// Walks a WebhookMessageContent looking for the new body of an edited message.
        /// Handles both shallow (editedMessage.message.conversation — used by messages.update)
        /// and deep (editedMessage.message.protocolMessage.editedMessage.conversation — used
        /// by some Baileys upsert deliveries) shapes.
        /// </summary>
        private static string? ExtractEditedBody(WebhookMessageContent? content)
        {
            if (content is null) return null;

            // Direct body fields on this level.
            var direct = content.Conversation
                       ?? content.ExtendedTextMessage?.Text
                       ?? content.ImageMessage?.Caption
                       ?? content.VideoMessage?.Caption
                       ?? content.DocumentMessage?.Caption
                       ?? content.DocumentWithCaptionMessage?.Message?.DocumentMessage?.Caption;
            if (!string.IsNullOrEmpty(direct)) return direct;

            // Recurse into a nested editedMessage.message wrapper.
            var nested = content.EditedMessage?.Message;
            if (nested is not null)
            {
                var fromNested = ExtractEditedBody(nested);
                if (!string.IsNullOrEmpty(fromNested)) return fromNested;
            }

            // Recurse into a protocolMessage.editedMessage.
            var fromProtocol = ExtractEditedBody(content.ProtocolMessage?.EditedMessage);
            return fromProtocol;
        }

        /// <summary>
        /// Inspects the Baileys message envelope and extracts (kind, text, mime, filename).
        /// Text falls back to caption for images/videos/documents so the conversation list
        /// still has something readable as a preview.
        /// </summary>
        private static (MessageKind kind, string? body, string? mime, string? fileName) ResolveMessagePayload(WebhookMessageData data)
        {
            var msg = data.Message!;

            if (!string.IsNullOrEmpty(msg.Conversation))
                return (MessageKind.Text, msg.Conversation, null, null);

            if (msg.ExtendedTextMessage?.Text is { Length: > 0 } extText)
                return (MessageKind.Text, extText, null, null);

            if (msg.ImageMessage is not null)
                return (MessageKind.Image, msg.ImageMessage.Caption, msg.ImageMessage.Mimetype, null);

            if (msg.VideoMessage is not null)
                return (MessageKind.Video, msg.VideoMessage.Caption, msg.VideoMessage.Mimetype, null);

            if (msg.AudioMessage is not null)
                return (MessageKind.Audio, null, msg.AudioMessage.Mimetype, null);

            if (msg.StickerMessage is not null)
                return (MessageKind.Sticker, null, msg.StickerMessage.Mimetype, null);

            if (msg.DocumentMessage is not null)
                return (MessageKind.Document, msg.DocumentMessage.Caption, msg.DocumentMessage.Mimetype, msg.DocumentMessage.FileName);

            // WhatsApp wraps captioned documents in this nested envelope.
            var docInner = msg.DocumentWithCaptionMessage?.Message?.DocumentMessage;
            if (docInner is not null)
                return (MessageKind.Document, docInner.Caption, docInner.Mimetype, docInner.FileName);

            return (MessageKind.Text, null, null, null);
        }

        /// <summary>
        /// Calls Evolution's /chat/getBase64FromMediaMessage/{instance} endpoint to decrypt
        /// the WhatsApp media, then writes it under wwwroot/media/{messageId}.{ext} and
        /// returns the public URL. Returns null on failure (the message row already exists,
        /// so the chat will show a placeholder bubble).
        /// </summary>
        private async Task<string?> DownloadAndStoreMediaAsync(
            string instanceName,
            string externalMessageId,
            int messageId,
            string? mimeType,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var requestPayload = new
                {
                    message = new { key = new { id = externalMessageId } },
                    convertToMp4 = false
                };
                var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"/chat/getBase64FromMediaMessage/{instanceName}", content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution media fetch failed {Status} for msg {Id}: {Err}",
                        response.StatusCode, externalMessageId, err);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!doc.RootElement.TryGetProperty("base64", out var b64Element))
                {
                    _logger.LogError("Evolution media response missing 'base64' for msg {Id}", externalMessageId);
                    return null;
                }

                var b64 = b64Element.GetString();
                if (string.IsNullOrEmpty(b64))
                    return null;

                var bytes = Convert.FromBase64String(b64);

                var ext = ExtensionForMime(mimeType);
                var rootForMedia = string.IsNullOrWhiteSpace(_env.WebRootPath)
                    ? Path.Combine(_env.ContentRootPath, "wwwroot")
                    : _env.WebRootPath;
                var mediaDir = Path.Combine(rootForMedia, "media");
                Directory.CreateDirectory(mediaDir);

                var fileName = $"{messageId}{ext}";
                var fullPath = Path.Combine(mediaDir, fileName);
                await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

                return $"/media/{fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download media for message {Id}", externalMessageId);
                return null;
            }
        }

        private static string ExtensionForMime(string? mime)
        {
            if (string.IsNullOrEmpty(mime)) return ".bin";
            var bare = mime.Split(';')[0].Trim().ToLowerInvariant();
            return bare switch
            {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "video/mp4" => ".mp4",
                "video/3gpp" => ".3gp",
                "video/webm" => ".webm",
                "audio/ogg" or "audio/ogg; codecs=opus" => ".ogg",
                "audio/mpeg" => ".mp3",
                "audio/mp4" => ".m4a",
                "audio/wav" => ".wav",
                "application/pdf" => ".pdf",
                "application/zip" => ".zip",
                "application/msword" => ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.ms-excel" => ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "text/plain" => ".txt",
                _ => ".bin"
            };
        }
    }
}
