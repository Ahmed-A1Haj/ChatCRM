using System.Globalization;
using System.Text;
using ChatCRM.Application.Contacts.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public class ContactsService : IContactsService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ContactsService> _logger;

        public ContactsService(AppDbContext db, ILogger<ContactsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ContactsListResultDto> ListAsync(ContactsListQuery query, CancellationToken cancellationToken = default)
        {
            var rows = BuildBaseQuery(query);

            var total = await rows.CountAsync(cancellationToken);

            rows = ApplySort(rows, query);

            var page     = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var items = await rows
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new ContactsListResultDto
            {
                Items = items,
                Total = total,
                Page  = page,
                PageSize = pageSize
            };
        }

        public async Task<byte[]> ExportCsvAsync(ContactsListQuery query, CancellationToken cancellationToken = default)
        {
            // Same filters as ListAsync but no paging — export everything matching.
            var rows = BuildBaseQuery(query);
            rows = ApplySort(rows, query);
            var all = await rows.ToListAsync(cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine("Name,Phone,Channel,Last Message,Created At,Country,Language,Lifecycle,Assigned To,Status,Blocked");

            foreach (var c in all)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(c.DisplayName ?? c.PhoneNumber),
                    Csv("+" + c.PhoneNumber),
                    Csv(c.Channel),
                    Csv(c.LastMessageAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? ""),
                    Csv(c.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                    Csv(c.Country ?? ""),
                    Csv(c.Language ?? ""),
                    Csv(StageLabel(c.LifecycleStage)),
                    Csv(c.AssignedUserName ?? ""),
                    Csv(c.ConversationStatus.HasValue ? StatusLabel(c.ConversationStatus.Value) : ""),
                    Csv(c.IsBlocked ? "Yes" : "No")
                }));
            }

            // UTF-8 with BOM so Excel renders accents and non-Latin characters correctly.
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public async Task SetLifecycleAsync(int contactId, byte stage, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(typeof(LifecycleStage), stage))
                throw new InvalidOperationException("Invalid lifecycle stage.");

            var contact = await _db.WhatsAppContacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken)
                ?? throw new InvalidOperationException($"Contact {contactId} not found.");

            contact.LifecycleStage = (LifecycleStage)stage;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task SetAssigneeAsync(int contactId, string? userId, CancellationToken cancellationToken = default)
        {
            // Apply assignment to ALL conversations of this contact so the contact-level
            // edit reflects across every channel/instance the contact has used.
            var conversations = await _db.Conversations
                .Where(c => c.ContactId == contactId)
                .ToListAsync(cancellationToken);

            if (conversations.Count == 0)
                throw new InvalidOperationException("Contact has no conversations to assign.");

            foreach (var c in conversations)
                c.AssignedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId;

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task SetStatusAsync(int contactId, byte status, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(typeof(ConversationStatus), status))
                throw new InvalidOperationException("Invalid status.");

            var conversations = await _db.Conversations
                .Where(c => c.ContactId == contactId)
                .ToListAsync(cancellationToken);

            if (conversations.Count == 0)
                throw new InvalidOperationException("Contact has no conversations.");

            var newStatus = (ConversationStatus)status;
            foreach (var c in conversations)
                c.Status = newStatus;

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task SetLanguageAsync(int contactId, string? language, CancellationToken cancellationToken = default)
        {
            var contact = await _db.WhatsAppContacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken)
                ?? throw new InvalidOperationException($"Contact {contactId} not found.");
            contact.Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task SetBlockAsync(int contactId, bool blocked, CancellationToken cancellationToken = default)
        {
            var contact = await _db.WhatsAppContacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken)
                ?? throw new InvalidOperationException($"Contact {contactId} not found.");
            contact.IsBlocked = blocked;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(int contactId, CancellationToken cancellationToken = default)
        {
            // Manual cascade: messages → conversations → contact (FKs are Restrict).
            var conversationIds = await _db.Conversations
                .Where(c => c.ContactId == contactId)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            if (conversationIds.Count > 0)
            {
                await _db.Messages
                    .Where(m => conversationIds.Contains(m.ConversationId))
                    .ExecuteDeleteAsync(cancellationToken);

                await _db.Conversations
                    .Where(c => c.ContactId == contactId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await _db.WhatsAppContacts
                .Where(c => c.Id == contactId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private IQueryable<ContactRowDto> BuildBaseQuery(ContactsListQuery query)
        {
            // Aggregate per contact: pick the most-recent conversation as "primary" for assignee/status.
            var rows = from contact in _db.WhatsAppContacts
                       let primary = _db.Conversations
                                        .Where(c => c.ContactId == contact.Id)
                                        .OrderByDescending(c => c.LastMessageAt)
                                        .FirstOrDefault()
                       let lastAt = _db.Conversations
                                        .Where(c => c.ContactId == contact.Id)
                                        .Max(c => (DateTime?)c.LastMessageAt)
                       let lastBody = primary != null
                            ? _db.Messages
                                .Where(m => m.ConversationId == primary.Id && m.Direction != MessageDirection.Note)
                                .OrderByDescending(m => m.SentAt)
                                .Select(m => m.Body)
                                .FirstOrDefault()
                            : null
                       select new ContactRowDto
                       {
                           Id = contact.Id,
                           PhoneNumber = contact.PhoneNumber,
                           DisplayName = contact.DisplayName,
                           Channel = primary != null ? primary.Instance.DisplayName : "—",
                           ChannelType = primary != null ? (byte)primary.Instance.ChannelType : (byte)0,
                           LastMessageAt = lastAt,
                           LastMessagePreview = lastBody,
                           CreatedAt = contact.CreatedAt,
                           Country = contact.Country,
                           Language = contact.Language,
                           LifecycleStage = (byte)contact.LifecycleStage,
                           AssignedUserId = primary != null ? primary.AssignedUserId : null,
                           AssignedUserName = primary != null && primary.AssignedUser != null
                               ? (string.IsNullOrEmpty(primary.AssignedUser.FirstName)
                                  ? primary.AssignedUser.Email
                                  : primary.AssignedUser.FirstName + " " + primary.AssignedUser.LastName)
                               : null,
                           ConversationStatus = primary != null ? (byte?)primary.Status : null,
                           IsBlocked = contact.IsBlocked,
                           PrimaryConversationId = primary != null ? primary.Id : null,
                           PrimaryInstanceId = primary != null ? primary.WhatsAppInstanceId : null
                       };

            // Filters
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim().ToLower();
                rows = rows.Where(r =>
                    (r.DisplayName != null && r.DisplayName.ToLower().Contains(s)) ||
                    r.PhoneNumber.Contains(s) ||
                    (r.Country != null && r.Country.ToLower().Contains(s)) ||
                    (r.Language != null && r.Language.ToLower().Contains(s)) ||
                    r.Channel.ToLower().Contains(s));
            }

            if (query.Lifecycle.HasValue)
                rows = rows.Where(r => r.LifecycleStage == query.Lifecycle.Value);

            if (!string.IsNullOrWhiteSpace(query.AssignedUserId))
                rows = rows.Where(r => r.AssignedUserId == query.AssignedUserId);

            if (query.Status.HasValue)
                rows = rows.Where(r => r.ConversationStatus == query.Status.Value);

            if (query.Blocked.HasValue)
                rows = rows.Where(r => r.IsBlocked == query.Blocked.Value);

            return rows;
        }

        private static IQueryable<ContactRowDto> ApplySort(IQueryable<ContactRowDto> rows, ContactsListQuery q)
        {
            bool asc = string.Equals(q.Direction, "asc", StringComparison.OrdinalIgnoreCase);

            return (q.Sort ?? "lastMessage").ToLowerInvariant() switch
            {
                "name"        => asc ? rows.OrderBy(r => r.DisplayName ?? r.PhoneNumber)
                                     : rows.OrderByDescending(r => r.DisplayName ?? r.PhoneNumber),
                "createdat"   => asc ? rows.OrderBy(r => r.CreatedAt)
                                     : rows.OrderByDescending(r => r.CreatedAt),
                "country"     => asc ? rows.OrderBy(r => r.Country ?? "")
                                     : rows.OrderByDescending(r => r.Country ?? ""),
                _             => asc ? rows.OrderBy(r => r.LastMessageAt ?? r.CreatedAt)
                                     : rows.OrderByDescending(r => r.LastMessageAt ?? r.CreatedAt)
            };
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string StageLabel(byte stage) => (LifecycleStage)stage switch
        {
            LifecycleStage.NewClient          => "New Client",
            LifecycleStage.NotResponding      => "Not Responding",
            LifecycleStage.Interested         => "Interested",
            LifecycleStage.Thinking           => "Thinking",
            LifecycleStage.WantsAMeeting      => "Wants a Meeting",
            LifecycleStage.WaitingForMeeting  => "Waiting for Meeting",
            LifecycleStage.Discussed          => "Discussed",
            LifecycleStage.PotentialClient    => "Potential Client",
            LifecycleStage.WillMakePayment    => "Will Make Payment",
            LifecycleStage.WaitingForContract => "Waiting for Contract",
            LifecycleStage.OurClient          => "Our Client",
            _ => "Unknown"
        };

        private static string StatusLabel(byte status) => status switch
        {
            0 => "Open",
            1 => "Snoozed",
            2 => "Closed",
            _ => "—"
        };
    }
}
