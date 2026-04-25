using System.Text;
using System.Text.Json;
using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
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
        private readonly ILogger<EvolutionService> _logger;

        public EvolutionService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IHubContext<ChatHub> hub,
            ILogger<EvolutionService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _hub = hub;
            _logger = logger;
        }

        public async Task<bool> SendMessageAsync(string instanceName, string phone, string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                _logger.LogError("SendMessageAsync called with empty instanceName.");
                return false;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var payload = new { number = phone, text = message };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"/message/sendText/{instanceName}", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution API error {Status} for {Instance}: {Body}", response.StatusCode, instanceName, error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp message to {Phone} via {Instance}", phone, instanceName);
                return false;
            }
        }

        public async Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default)
        {
            if (payload.Data?.Key is null || payload.Data.Message is null)
                return;

            if (payload.Data.Key.FromMe)
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

            var externalId = payload.Data.Key.Id;

            // Deduplicate
            var alreadyProcessed = await _db.Messages
                .AnyAsync(m => m.ExternalId == externalId, cancellationToken);

            if (alreadyProcessed)
                return;

            var rawJid = payload.Data.Key.RemoteJid;
            var phone = rawJid.Split('@')[0];

            var body = payload.Data.Message.Conversation
                ?? payload.Data.Message.ExtendedTextMessage?.Text
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(body))
                return;

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
                    DisplayName = payload.Data.PushName,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WhatsAppContacts.Add(contact);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (contact.DisplayName is null && payload.Data.PushName is not null)
            {
                contact.DisplayName = payload.Data.PushName;
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
                Body = body,
                Direction = MessageDirection.Incoming,
                Status = MessageStatus.Sent,
                ExternalId = externalId,
                SentAt = sentAt
            };

            _db.Messages.Add(message);
            conversation.LastMessageAt = sentAt;
            conversation.UnreadCount += 1;

            await _db.SaveChangesAsync(cancellationToken);

            // Broadcast to the SignalR group for this specific instance.
            await _hub.Clients.Group(ChatHub.InstanceGroupName(instance.Id))
                .SendAsync("ReceiveMessage", new
                {
                    instanceId = instance.Id,
                    conversationId = conversation.Id,
                    contactPhone = contact.PhoneNumber,
                    contactName = contact.DisplayName,
                    message = new
                    {
                        id = message.Id,
                        body = message.Body,
                        direction = (int)message.Direction,
                        sentAt = message.SentAt
                    },
                    unreadCount = conversation.UnreadCount
                }, cancellationToken);
        }
    }
}
