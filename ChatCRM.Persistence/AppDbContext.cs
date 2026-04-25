using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChatCRM.Persistence
{
    public class AppDbContext : IdentityDbContext<User>, IAppDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<WhatsAppContact> WhatsAppContacts => Set<WhatsAppContact>();
        public DbSet<WhatsAppInstance> WhatsAppInstances => Set<WhatsAppInstance>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<Message> Messages => Set<Message>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(builder =>
            {
                builder.Property(x => x.FirstName).HasMaxLength(100);
                builder.Property(x => x.LastName).HasMaxLength(100);
                builder.Property(x => x.ProfileImagePath).HasMaxLength(260);
            });

            modelBuilder.Entity<WhatsAppContact>(builder =>
            {
                builder.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
                builder.HasIndex(x => x.PhoneNumber).IsUnique();
                builder.Property(x => x.DisplayName).HasMaxLength(100);
                builder.Property(x => x.AvatarUrl).HasMaxLength(260);
            });

            modelBuilder.Entity<WhatsAppInstance>(builder =>
            {
                builder.Property(x => x.InstanceName).HasMaxLength(100).IsRequired();
                builder.HasIndex(x => x.InstanceName).IsUnique();
                builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
                builder.Property(x => x.PhoneNumber).HasMaxLength(30);
                builder.Property(x => x.OwnerJid).HasMaxLength(100);

                builder.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Conversation>(builder =>
            {
                builder.HasOne(x => x.Contact)
                    .WithMany(x => x.Conversations)
                    .HasForeignKey(x => x.ContactId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(x => x.Instance)
                    .WithMany(x => x.Conversations)
                    .HasForeignKey(x => x.WhatsAppInstanceId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.HasOne(x => x.AssignedUser)
                    .WithMany()
                    .HasForeignKey(x => x.AssignedUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.HasIndex(x => x.LastMessageAt);
                builder.HasIndex(x => new { x.WhatsAppInstanceId, x.LastMessageAt });
                builder.HasIndex(x => new { x.ContactId, x.WhatsAppInstanceId }).IsUnique();
            });

            modelBuilder.Entity<Message>(builder =>
            {
                builder.HasOne(x => x.Conversation)
                    .WithMany(x => x.Messages)
                    .HasForeignKey(x => x.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.Property(x => x.ExternalId).HasMaxLength(100);
                builder.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[ExternalId] IS NOT NULL");
                builder.HasIndex(x => x.ConversationId);
                builder.HasIndex(x => x.SentAt);
            });
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
