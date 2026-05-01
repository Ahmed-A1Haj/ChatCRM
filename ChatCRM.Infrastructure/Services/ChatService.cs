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

        public async Task<List<ConversationDto>> GetConversationsAsync(int? instanceId = null, string? filter = null, string? currentUserId = null, byte? channelType = null, CancellationToken cancellationToken = default)
        {
            var query = _db.Conversations.AsQueryable();

            if (channelType.HasValue)
            {
                var ct = (ChannelType)channelType.Value;
                query = query.Where(c => c.Instance.ChannelType == ct);
            }

            // Inbox filter — closed conversations stay visible (with a "Closed" pill on the row);
            // the dedicated "closed" filter still narrows to just those if the user wants it.
            switch ((filter ?? "all").ToLowerInvariant())
            {
                case "mine":
                    if (!string.IsNullOrEmpty(currentUserId))
                        query = query.Where(c => c.AssignedUserId == currentUserId);
                    break;
                case "unassigned":
                    query = query.Where(c => c.AssignedUserId == null);
                    break;
                case "closed":
                    query = query.Where(c => c.Status == ConversationStatus.Closed);
                    break;
                case "all":
                default:
                    /* show everything regardless of status */
                    break;
            }

            if (instanceId.HasValue)
                query = query.Where(c => c.WhatsAppInstanceId == instanceId.Value);

            return await query
                .OrderByDescending(c => c.LastMessageAt)
                .Select(c => new ConversationDto
                {
                    Id = c.Id,
                    InstanceId = c.WhatsAppInstanceId,
                    InstanceDisplayName = c.Instance.DisplayName,
                    ChannelType = (byte)c.Instance.ChannelType,
                    PhoneNumber = c.Contact.PhoneNumber,
                    DisplayName = c.Contact.DisplayName,
                    AvatarUrl = c.Contact.AvatarUrl,
                    LastMessage = c.Messages
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Body)
                        .FirstOrDefault() ?? string.Empty,
                    LastMessageAt = c.LastMessageAt,
                    UnreadCount = c.UnreadCount,
                    IsArchived = c.IsArchived,
                    AssignedUserId = c.AssignedUserId,
                    AssignedUserName = c.AssignedUser != null
                        ? (string.IsNullOrWhiteSpace(c.AssignedUser.FirstName) ? c.AssignedUser.Email : c.AssignedUser.FirstName + " " + c.AssignedUser.LastName)
                        : null,
                    ConversationStatus = (byte)c.Status,
                    Tags = c.Tags.Select(t => new TagDto { Id = t.TagId, Name = t.Tag.Name, Color = t.Tag.Color }).ToList()
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
                    SentAt = m.SentAt,
                    AuthorName = m.AuthorUser != null
                        ? (string.IsNullOrWhiteSpace(m.AuthorUser.FirstName) ? m.AuthorUser.Email : m.AuthorUser.FirstName + " " + m.AuthorUser.LastName)
                        : null
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

        public async Task<MessageDto> AddNoteAsync(AddNoteDto dto, string authorUserId, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == dto.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {dto.ConversationId} not found.");

            var author = await _db.Users.FirstOrDefaultAsync(u => u.Id == authorUserId, cancellationToken);

            var note = new Message
            {
                ConversationId = conversation.Id,
                Body = dto.Body,
                Direction = MessageDirection.Note,
                Status = MessageStatus.Sent,
                SentAt = DateTime.UtcNow,
                AuthorUserId = authorUserId
            };

            _db.Messages.Add(note);
            await _db.SaveChangesAsync(cancellationToken);

            var authorName = author != null
                ? (string.IsNullOrWhiteSpace(author.FirstName) ? author.Email : $"{author.FirstName} {author.LastName}")
                : null;

            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("ReceiveMessage", new
                {
                    instanceId = conversation.WhatsAppInstanceId,
                    conversationId = conversation.Id,
                    message = new
                    {
                        id = note.Id,
                        body = note.Body,
                        direction = (int)note.Direction,
                        sentAt = note.SentAt,
                        authorName
                    },
                    unreadCount = conversation.UnreadCount
                }, cancellationToken);

            return new MessageDto
            {
                Id = note.Id,
                Body = note.Body,
                Direction = note.Direction,
                Status = note.Status,
                SentAt = note.SentAt,
                AuthorName = authorName
            };
        }

        public async Task AssignAsync(AssignDto dto, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == dto.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {dto.ConversationId} not found.");

            conversation.AssignedUserId = string.IsNullOrWhiteSpace(dto.UserId) ? null : dto.UserId;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("ConversationAssigned", new
                {
                    conversationId = conversation.Id,
                    instanceId = conversation.WhatsAppInstanceId,
                    assignedUserId = conversation.AssignedUserId
                }, cancellationToken);
        }

        public async Task SetStatusAsync(SetStatusDto dto, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == dto.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {dto.ConversationId} not found.");

            conversation.Status = (ConversationStatus)dto.Status;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("ConversationStatusChanged", new
                {
                    conversationId = conversation.Id,
                    instanceId = conversation.WhatsAppInstanceId,
                    status = (byte)conversation.Status
                }, cancellationToken);
        }

        public async Task SetLifecycleStageAsync(SetLifecycleDto dto, CancellationToken cancellationToken = default)
        {
            var conversation = await _db.Conversations
                .Include(c => c.Contact)
                .FirstOrDefaultAsync(c => c.Id == dto.ConversationId, cancellationToken)
                ?? throw new InvalidOperationException($"Conversation {dto.ConversationId} not found.");

            if (!Enum.IsDefined(typeof(LifecycleStage), dto.Stage))
                throw new InvalidOperationException($"Invalid lifecycle stage value {dto.Stage}.");

            conversation.Contact.LifecycleStage = (LifecycleStage)dto.Stage;
            await _db.SaveChangesAsync(cancellationToken);

            await _hub.Clients.Group(ChatHub.InstanceGroupName(conversation.WhatsAppInstanceId))
                .SendAsync("LifecycleStageChanged", new
                {
                    contactId = conversation.ContactId,
                    conversationId = conversation.Id,
                    instanceId = conversation.WhatsAppInstanceId,
                    stage = (byte)conversation.Contact.LifecycleStage
                }, cancellationToken);
        }

        public async Task<ContactDetailsDto?> GetContactDetailsAsync(int conversationId, CancellationToken cancellationToken = default)
        {
            var c = await _db.Conversations
                .Include(x => x.Contact)
                .Include(x => x.Instance)
                .Include(x => x.AssignedUser)
                .Include(x => x.Tags).ThenInclude(t => t.Tag)
                .FirstOrDefaultAsync(x => x.Id == conversationId, cancellationToken);

            if (c is null) return null;

            var msgCount = await _db.Messages
                .CountAsync(m => m.ConversationId == conversationId && m.Direction != MessageDirection.Note, cancellationToken);
            var noteCount = await _db.Messages
                .CountAsync(m => m.ConversationId == conversationId && m.Direction == MessageDirection.Note, cancellationToken);

            return new ContactDetailsDto
            {
                ConversationId = c.Id,
                ContactId = c.ContactId,
                PhoneNumber = c.Contact.PhoneNumber,
                DisplayName = c.Contact.DisplayName,
                AvatarUrl = c.Contact.AvatarUrl,
                ContactCreatedAt = c.Contact.CreatedAt,
                ConversationCreatedAt = c.CreatedAt,
                InstanceDisplayName = c.Instance.DisplayName,
                AssignedUserId = c.AssignedUserId,
                AssignedUserName = c.AssignedUser != null
                    ? (string.IsNullOrWhiteSpace(c.AssignedUser.FirstName) ? c.AssignedUser.Email : $"{c.AssignedUser.FirstName} {c.AssignedUser.LastName}")
                    : null,
                ConversationStatus = (byte)c.Status,
                LifecycleStage = (byte)c.Contact.LifecycleStage,
                MessageCount = msgCount,
                NoteCount = noteCount,
                Tags = c.Tags.Select(t => new TagDto { Id = t.TagId, Name = t.Tag.Name, Color = t.Tag.Color }).ToList()
            };
        }

        public async Task<List<TeamMemberDto>> GetTeamMembersAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Users
                .OrderBy(u => u.FirstName ?? u.Email)
                .Select(u => new TeamMemberDto
                {
                    Id = u.Id,
                    Name = string.IsNullOrWhiteSpace(u.FirstName)
                        ? (u.Email ?? u.UserName ?? "User")
                        : (u.FirstName + " " + u.LastName),
                    Email = u.Email
                })
                .ToListAsync(cancellationToken);
        }
    }
}
