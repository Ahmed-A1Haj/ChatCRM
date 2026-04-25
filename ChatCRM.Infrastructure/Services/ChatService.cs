using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _db;
        private readonly IEvolutionService _evolutionService;
        private readonly IHubContext<ChatHub> _hub;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            AppDbContext db,
            IEvolutionService evolutionService,
            IHubContext<ChatHub> hub,
            ILogger<ChatService> logger)
        {
            _db = db;
            _evolutionService = evolutionService;
            _hub = hub;
            _logger = logger;
        }

        public async Task<List<ConversationDto>> GetConversationsAsync(int? instanceId = null, CancellationToken cancellationToken = default)
        {
            var query = _db.Conversations.Where(c => !c.IsArchived);

            if (instanceId.HasValue)
                query = query.Where(c => c.WhatsAppInstanceId == instanceId.Value);

            return await query
                .OrderByDescending(c => c.LastMessageAt)
                .Select(c => new ConversationDto
                {
                    Id = c.Id,
                    InstanceId = c.WhatsAppInstanceId,
                    PhoneNumber = c.Contact.PhoneNumber,
                    DisplayName = c.Contact.DisplayName,
                    AvatarUrl = c.Contact.AvatarUrl,
                    LastMessage = c.Messages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Body)
                        .FirstOrDefault() ?? string.Empty,
                    LastMessageAt = c.LastMessageAt,
                    UnreadCount = c.UnreadCount,
                    IsArchived = c.IsArchived
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<List<MessageDto>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default)
        {
            return await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageDto
                {
                    Id = m.Id,
                    Body = m.Body,
                    Direction = m.Direction,
                    Status = m.Status,
                    SentAt = m.SentAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<MessageDto> SendMessageAsync(SendMessageDto dto, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations
                .Include(c => c.Contact)
                .Include(c => c.Instance)
                .FirstOrDefaultAsync(c => c.Id == dto.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {dto.ConversationId} not found.");

            var message = new Message
            {
                ConversationId = conversation.Id,
                Body = dto.Body,
                Direction = MessageDirection.Outgoing,
                Status = MessageStatus.Sent,
                SentAt = DateTime.UtcNow
            };

            _db.Messages.Add(message);
            conversation.LastMessageAt = message.SentAt;
            await _db.SaveChangesAsync(cancellationToken);

            var sent = await _evolutionService.SendMessageAsync(
                conversation.Instance.InstanceName,
                conversation.Contact.PhoneNumber,
                dto.Body,
                cancellationToken);

            if (!sent)
                _logger.LogWarning("Evolution API failed to deliver message {MessageId} via {Instance} to {Phone}",
                    message.Id, conversation.Instance.InstanceName, conversation.Contact.PhoneNumber);

            var instanceUnread = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == conversation.WhatsAppInstanceId && !c.IsArchived)
                .SumAsync(c => c.UnreadCount, cancellationToken);

            var instanceChatCount = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == conversation.WhatsAppInstanceId && !c.IsArchived)
                .CountAsync(cancellationToken);

            // Broadcast only to clients viewing this instance.
            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("ReceiveMessage", new
                {
                    instanceId = conversation.WhatsAppInstanceId,
                    instanceUnread,
                    instanceChatCount,
                    conversationId = conversation.Id,
                    contactPhone = conversation.Contact.PhoneNumber,
                    contactName = conversation.Contact.DisplayName,
                    message = new
                    {
                        id = message.Id,
                        body = message.Body,
                        direction = (int)message.Direction,
                        sentAt = message.SentAt
                    },
                    unreadCount = conversation.UnreadCount
                }, cancellationToken);

            return new MessageDto
            {
                Id = message.Id,
                Body = message.Body,
                Direction = message.Direction,
                Status = message.Status,
                SentAt = message.SentAt
            };
        }

        public async Task MarkAsReadAsync(int conversationId, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
            if (conversation is null) return;

            // No-op if already read — don't trigger pointless broadcasts.
            if (conversation.UnreadCount == 0) return;

            conversation.UnreadCount = 0;
            await _db.SaveChangesAsync(cancellationToken);

            // Aggregate the new unread total for this instance so all dashboards/dropdowns can update.
            var instanceUnread = await _db.Conversations
                .Where(c => c.WhatsAppInstanceId == conversation.WhatsAppInstanceId && !c.IsArchived)
                .SumAsync(c => c.UnreadCount, cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("ConversationRead", new
                {
                    conversationId = conversation.Id,
                    instanceId = conversation.WhatsAppInstanceId,
                    instanceUnread
                }, cancellationToken);
        }
    }
}
