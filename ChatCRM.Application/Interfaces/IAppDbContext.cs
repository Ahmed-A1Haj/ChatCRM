using ChatCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatCRM.Application.Interfaces
{
    public interface IAppDbContext
    {
        DbSet<User> Users { get; }
        DbSet<WhatsAppContact> WhatsAppContacts { get; }
        DbSet<WhatsAppInstance> WhatsAppInstances { get; }
        DbSet<Conversation> Conversations { get; }
        DbSet<Message> Messages { get; }
        DbSet<Tag> Tags { get; }
        DbSet<ConversationTag> ConversationTags { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }
}
