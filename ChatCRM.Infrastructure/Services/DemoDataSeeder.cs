using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public static class DemoDataSeeder
    {
        public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (await db.WhatsAppContacts.AnyAsync(cancellationToken))
            {
                logger.LogInformation("[SEED] Demo data already present — skipping.");
                return;
            }

            var defaultInstance = await db.WhatsAppInstances
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultInstance is null)
            {
                logger.LogWarning("[SEED] No WhatsApp instance found; cannot seed demo conversations.");
                return;
            }

            var instanceId = defaultInstance.Id;
            var now = DateTime.UtcNow;

            var alice = new WhatsAppContact
            {
                PhoneNumber = "15551010101",
                DisplayName = "Alice Johnson",
                CreatedAt = now.AddDays(-3)
            };

            var bob = new WhatsAppContact
            {
                PhoneNumber = "15551020202",
                DisplayName = "Bob Martinez",
                CreatedAt = now.AddDays(-2)
            };

            var carol = new WhatsAppContact
            {
                PhoneNumber = "15551030303",
                DisplayName = "Carol Singh",
                CreatedAt = now.AddHours(-8)
            };

            db.WhatsAppContacts.AddRange(alice, bob, carol);
            await db.SaveChangesAsync(cancellationToken);

            // Alice: a back-and-forth from yesterday, one unread
            var aliceConv = new Conversation { ContactId = alice.Id, WhatsAppInstanceId = instanceId, CreatedAt = now.AddDays(-1), LastMessageAt = now.AddMinutes(-6), UnreadCount = 1 };
            db.Conversations.Add(aliceConv);
            await db.SaveChangesAsync(cancellationToken);

            db.Messages.AddRange(
                new Message { ConversationId = aliceConv.Id, Body = "Hi! Is the website package still available?", Direction = MessageDirection.Incoming, SentAt = now.AddDays(-1).AddHours(-2) },
                new Message { ConversationId = aliceConv.Id, Body = "Yes, it is! Which tier were you interested in?", Direction = MessageDirection.Outgoing, Status = MessageStatus.Read, SentAt = now.AddDays(-1).AddHours(-2).AddMinutes(3) },
                new Message { ConversationId = aliceConv.Id, Body = "The business one. What's included?", Direction = MessageDirection.Incoming, SentAt = now.AddDays(-1).AddHours(-1).AddMinutes(-45) },
                new Message { ConversationId = aliceConv.Id, Body = "It covers 10 pages, SEO setup, and 3 months of maintenance. Shall I send the quote?", Direction = MessageDirection.Outgoing, Status = MessageStatus.Read, SentAt = now.AddDays(-1).AddHours(-1).AddMinutes(-40) },
                new Message { ConversationId = aliceConv.Id, Body = "Yes please!", Direction = MessageDirection.Incoming, SentAt = now.AddMinutes(-6) }
            );

            // Bob: read thread from 2 hours ago
            var bobConv = new Conversation { ContactId = bob.Id, WhatsAppInstanceId = instanceId, CreatedAt = now.AddHours(-5), LastMessageAt = now.AddHours(-2), UnreadCount = 0 };
            db.Conversations.Add(bobConv);
            await db.SaveChangesAsync(cancellationToken);

            db.Messages.AddRange(
                new Message { ConversationId = bobConv.Id, Body = "Quick question about billing", Direction = MessageDirection.Incoming, SentAt = now.AddHours(-5) },
                new Message { ConversationId = bobConv.Id, Body = "Of course — go ahead.", Direction = MessageDirection.Outgoing, Status = MessageStatus.Read, SentAt = now.AddHours(-5).AddMinutes(1) },
                new Message { ConversationId = bobConv.Id, Body = "Can I get an invoice with VAT broken out?", Direction = MessageDirection.Incoming, SentAt = now.AddHours(-4).AddMinutes(-55) },
                new Message { ConversationId = bobConv.Id, Body = "Absolutely. I'll email that over today.", Direction = MessageDirection.Outgoing, Status = MessageStatus.Read, SentAt = now.AddHours(-2) }
            );

            // Carol: brand new, 3 unread
            var carolConv = new Conversation { ContactId = carol.Id, WhatsAppInstanceId = instanceId, CreatedAt = now.AddMinutes(-30), LastMessageAt = now.AddMinutes(-2), UnreadCount = 3 };
            db.Conversations.Add(carolConv);
            await db.SaveChangesAsync(cancellationToken);

            db.Messages.AddRange(
                new Message { ConversationId = carolConv.Id, Body = "Hello!", Direction = MessageDirection.Incoming, SentAt = now.AddMinutes(-30) },
                new Message { ConversationId = carolConv.Id, Body = "Saw your ad — do you do branding too?", Direction = MessageDirection.Incoming, SentAt = now.AddMinutes(-10) },
                new Message { ConversationId = carolConv.Id, Body = "And logo design?", Direction = MessageDirection.Incoming, SentAt = now.AddMinutes(-2) }
            );

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("[SEED] Seeded 3 contacts, 3 conversations, 12 messages.");
        }
    }
}
