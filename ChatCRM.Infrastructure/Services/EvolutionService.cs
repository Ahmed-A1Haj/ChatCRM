using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services
{
    public class EvolutionService : IEvolutionService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EvolutionOptions _options;
        private readonly IHubContext<ChatHub> _hub;
        private readonly ILogger<EvolutionService> _logger;

        public EvolutionService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IOptions<EvolutionOptions> options,
            IHubContext<ChatHub> hub,
            ILogger<EvolutionService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _hub = hub;
            _logger = logger;
        }

        public async Task<bool> SendMessageAsync(string phone, string message, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("Evolution");
                var payload = new
                {
                    number = phone,
                    text = message
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    $"/message/sendText/{_options.InstanceName}",
                    content,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Evolution API error {Status}: {Body}", response.StatusCode, error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp message to {Phone}", phone);
                return false;
            }
        }

        public async Task HandleIncomingWebhookAsync(WebhookPayloadDto payload, CancellationToken cancellationToken = default)
        {
            if (payload.Data?.Key is null || payload.Data.Message is null)
                return;

            // Only handle inbound messages (not echoes of our own sends)
            if (payload.Data.Key.FromMe)
                return;

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

            // Upsert contact
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

            // Upsert conversation
            var conversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ContactId == contact.Id, cancellationToken);

            if (conversation is null)
            {
                conversation = new Conversation
                {
                    ContactId = contact.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Conversations.Add(conversation);
                await _db.SaveChangesAsync(cancellationToken);
            }

            // Store message
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

            // Push real-time update via SignalR
            await _hub.Clients.All.SendAsync("ReceiveMessage", new
            {
                conversationId = conversation.Id,
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
