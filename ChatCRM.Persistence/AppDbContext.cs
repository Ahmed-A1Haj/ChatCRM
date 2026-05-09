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
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<ConversationTag> ConversationTags => Set<ConversationTag>();
        public DbSet<Wallet> Wallets => Set<Wallet>();
        public DbSet<MetaPricingRule> MetaPricingRules => Set<MetaPricingRule>();
        public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();
        public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
        public DbSet<BillingAuditLog> BillingAuditLogs => Set<BillingAuditLog>();
        public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
        public DbSet<MessageBillingRecord> MessageBillingRecords => Set<MessageBillingRecord>();
        public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Agent> Agents => Set<Agent>();

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
                builder.Property(x => x.Country).HasMaxLength(60);
                builder.Property(x => x.Language).HasMaxLength(40);
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

                builder.HasOne(x => x.AssignedAgent)
                    .WithMany()
                    .HasForeignKey(x => x.AssignedAgentId)
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

                builder.HasOne(x => x.AuthorUser)
                    .WithMany()
                    .HasForeignKey(x => x.AuthorUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.Property(x => x.ExternalId).HasMaxLength(100);
                builder.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[ExternalId] IS NOT NULL");
                builder.HasIndex(x => x.ConversationId);
                builder.HasIndex(x => x.SentAt);
            });

            modelBuilder.Entity<Tag>(builder =>
            {
                builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
                builder.HasIndex(x => x.Name).IsUnique();
                builder.Property(x => x.Color).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<ConversationTag>(builder =>
            {
                builder.HasKey(x => new { x.ConversationId, x.TagId });

                builder.HasOne(x => x.Conversation)
                    .WithMany(x => x.Tags)
                    .HasForeignKey(x => x.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(x => x.Tag)
                    .WithMany(x => x.ConversationTags)
                    .HasForeignKey(x => x.TagId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MetaPricingRule>(builder =>
            {
                builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
                builder.Property(x => x.BasePriceUsd).HasColumnType("decimal(18,6)").IsRequired();
                builder.Property(x => x.Source).HasMaxLength(120);

                // Hot lookup path: "active rule for (category, country) at time T"
                builder.HasIndex(x => new { x.Category, x.CountryCode, x.EffectiveFrom });
            });

            modelBuilder.Entity<BillingSettings>(builder =>
            {
                builder.Property(x => x.MarkupPercentage).HasColumnType("decimal(5,2)").IsRequired();
                builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
                builder.Property(x => x.RefundPolicy).HasMaxLength(2000).IsRequired();
                builder.Property(x => x.InvoiceFooter).HasMaxLength(2000);

                // Singleton per workspace — same invariant as Wallet.
                builder.HasIndex(x => x.WorkspaceId).IsUnique();
            });

            modelBuilder.Entity<WalletTransaction>(builder =>
            {
                builder.Property(x => x.AmountUsd).HasColumnType("decimal(18,4)").IsRequired();
                builder.Property(x => x.BalanceAfterUsd).HasColumnType("decimal(18,4)").IsRequired();
                // Reference holds external ids (Stripe Checkout sessions, PaymentIntents, …) —
                // some go past 100 chars on newer Stripe API versions. 256 leaves comfortable
                // headroom without forcing nvarchar(max).
                builder.Property(x => x.Reference).HasMaxLength(256);
                builder.Property(x => x.Reason).HasMaxLength(500);

                builder.HasOne(x => x.Wallet)
                    .WithMany()
                    .HasForeignKey(x => x.WalletId)
                    .OnDelete(DeleteBehavior.Restrict);

                builder.HasOne(x => x.Message)
                    .WithMany()
                    .HasForeignKey(x => x.MessageId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.HasOne(x => x.InitiatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.InitiatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Hot paths: per-wallet ledger view, recent activity ticker.
                builder.HasIndex(x => new { x.WalletId, x.CreatedAt });
                builder.HasIndex(x => x.Reference);
            });

            modelBuilder.Entity<MessageBillingRecord>(builder =>
            {
                builder.Property(x => x.MetaCostUsd).HasColumnType("decimal(18,6)").IsRequired();
                builder.Property(x => x.ChargedUsd).HasColumnType("decimal(18,6)").IsRequired();
                builder.Property(x => x.MarkupPercentageAtTime).HasColumnType("decimal(5,2)").IsRequired();
                builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
                builder.Property(x => x.AppliedRuleLabel).HasMaxLength(80).IsRequired();

                builder.HasOne(x => x.Message)
                    .WithMany()
                    .HasForeignKey(x => x.MessageId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasOne(x => x.WalletTransaction)
                    .WithMany()
                    .HasForeignKey(x => x.WalletTransactionId)
                    .OnDelete(DeleteBehavior.SetNull);

                // One billing row per message — duplicate inserts (e.g. webhook retry) are blocked.
                builder.HasIndex(x => x.MessageId).IsUnique();
                // Hot path: month-to-date aggregation by category for the admin reports.
                builder.HasIndex(x => new { x.Category, x.CreatedAt });
            });

            modelBuilder.Entity<Agent>(builder =>
            {
                builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
                builder.Property(x => x.Description).HasMaxLength(500);
                builder.Property(x => x.AvatarPath).HasMaxLength(260);

                builder.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Name uniqueness per workspace — fail fast at insert rather than letting two
                // agents share the display name.
                builder.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();

                // Filtered unique index: at most one row per workspace can have IsDefault = 1.
                // Postgres calls this a partial index; SQL Server supports it via HasFilter.
                // The service layer also enforces this, but the index is the authoritative guard
                // against race conditions on concurrent set-default calls.
                builder.HasIndex(x => x.WorkspaceId)
                    .HasFilter("[IsDefault] = 1")
                    .IsUnique()
                    .HasDatabaseName("IX_Agents_WorkspaceId_DefaultUnique");

                builder.HasIndex(x => new { x.WorkspaceId, x.IsActive });
            });

            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.Property(x => x.Number).HasMaxLength(40).IsRequired();
                builder.Property(x => x.TotalChargedUsd).HasColumnType("decimal(18,4)");
                builder.Property(x => x.TotalMetaCostUsd).HasColumnType("decimal(18,4)");
                builder.Property(x => x.TotalToppedUpUsd).HasColumnType("decimal(18,4)");

                builder.HasOne(x => x.IssuedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.IssuedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Workspace + Number is the human identifier — must be unique per workspace.
                builder.HasIndex(x => new { x.WorkspaceId, x.Number }).IsUnique();
                builder.HasIndex(x => new { x.WorkspaceId, x.PeriodStartUtc });
            });

            modelBuilder.Entity<WhatsAppTemplate>(builder =>
            {
                builder.Property(x => x.Name).HasMaxLength(512).IsRequired();
                builder.Property(x => x.LanguageCode).HasMaxLength(20).IsRequired();
                builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
                builder.Property(x => x.Footer).HasMaxLength(60);
                builder.Property(x => x.MetaTemplateId).HasMaxLength(64);
                builder.Property(x => x.RejectionReason).HasMaxLength(2000);

                builder.HasOne(x => x.SubmittedViaInstance)
                    .WithMany()
                    .HasForeignKey(x => x.SubmittedViaInstanceId)
                    .OnDelete(DeleteBehavior.SetNull);

                builder.HasOne(x => x.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.CreatedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Meta enforces (Name, Language) uniqueness within a WABA — fail fast locally
                // before submission rather than letting the Graph API return an opaque 400.
                builder.HasIndex(x => new { x.WorkspaceId, x.Name, x.LanguageCode }).IsUnique();
                builder.HasIndex(x => new { x.WorkspaceId, x.Status });
                // Polling looks for Submitted rows ordered by SubmittedAt — the (Status, SubmittedAt)
                // composite covers it without scanning the table.
                builder.HasIndex(x => new { x.Status, x.SubmittedAt });
            });

            modelBuilder.Entity<ProcessedStripeEvent>(builder =>
            {
                builder.HasKey(x => x.EventId);
                builder.Property(x => x.EventId).HasMaxLength(64).IsRequired();
                builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
                builder.HasIndex(x => x.ProcessedAtUtc);
            });

            modelBuilder.Entity<BillingAuditLog>(builder =>
            {
                builder.Property(x => x.Actor).HasMaxLength(200).IsRequired();
                builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
                builder.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
                // EntityId often holds an external provider id (Stripe session/intent/charge)
                // and those run past 64 chars. 256 matches WalletTransaction.Reference.
                builder.Property(x => x.EntityId).HasMaxLength(256).IsRequired();
                // BeforeJson/AfterJson left as nvarchar(max) — sometimes contains the full entity snapshot.

                builder.HasIndex(x => x.AtUtc);
                builder.HasIndex(x => new { x.EntityType, x.EntityId });
            });

            modelBuilder.Entity<Wallet>(builder =>
            {
                // Currency stored as fixed-point decimal — never float/double for money.
                builder.Property(x => x.BalanceUsd).HasColumnType("decimal(18,4)").IsRequired();
                builder.Property(x => x.LowBalanceThresholdUsd).HasColumnType("decimal(18,4)").IsRequired();
                builder.Property(x => x.AutoRechargeAmountUsd).HasColumnType("decimal(18,4)");
                builder.Property(x => x.AutoRechargeTriggerUsd).HasColumnType("decimal(18,4)");
                builder.Property(x => x.DisplayCurrency).HasMaxLength(3).IsRequired();
                builder.Property(x => x.StripeCustomerId).HasMaxLength(64);
                builder.Property(x => x.DefaultPaymentMethodId).HasMaxLength(64);

                // Singleton invariant for v1: one wallet per WorkspaceId. Unique index enforces it
                // even if two requests race on the seeder.
                builder.HasIndex(x => x.WorkspaceId).IsUnique();

                // Optimistic concurrency token — surfaces as SQL Server `rowversion` (timestamp).
                builder.Property(x => x.RowVersion).IsRowVersion();
            });
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
