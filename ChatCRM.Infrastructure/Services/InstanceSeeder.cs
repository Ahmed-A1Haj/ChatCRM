using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public static class InstanceSeeder
    {
        /// <summary>
        /// If the DB has no WhatsAppInstances at all, create one that points to the legacy
        /// single-instance name configured in appsettings. Lets the app boot cleanly on fresh installs.
        /// </summary>
        public static async Task SeedDefaultIfEmptyAsync(AppDbContext db, string? configuredInstanceName, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (await db.WhatsAppInstances.AnyAsync(cancellationToken))
                return;

            var name = string.IsNullOrWhiteSpace(configuredInstanceName) ? "chatcrm" : configuredInstanceName;

            db.WhatsAppInstances.Add(new WhatsAppInstance
            {
                InstanceName = name,
                DisplayName = "Main Line",
                Status = InstanceStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("[SEED] Created default WhatsApp instance '{Name}'.", name);
        }
    }
}
