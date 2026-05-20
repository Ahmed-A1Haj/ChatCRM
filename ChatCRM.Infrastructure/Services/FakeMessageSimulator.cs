using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Dev-only background service: injects a fake inbound message into a random conversation every 45s.
    /// Lets the dashboard's SignalR real-time flow be exercised without a real WhatsApp connection.
    /// </summary>
    public class FakeMessageSimulator : BackgroundService
    {
        private static readonly string[] Samples =
        {
            "Just following up on this 👋",
            "Any news?",
            "Also — do you ship internationally?",
            "Sorry, one more question",
            "Thanks so much!",
            "What's your lead time on orders?",
            "Can you send pricing?",
            "Perfect, that works for me."
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FakeMessageSimulator> _logger;

        public FakeMessageSimulator(IServiceScopeFactory scopeFactory, ILogger<FakeMessageSimulator> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the app finish starting + seeding before the first tick
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            var rng = new Random();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub>>();

                    var conversations = await db.Conversations
                        .Where(c => !c.IsArchived)
                        .Select(c => c.Id)
                        .ToListAsync(stoppingToken);

                    if (conversations.Count > 0)
                    {
                        var convId = conversations[rng.Next(conversations.Count)];
                        var body = Samples[rng.Next(Samples.Length)];

                        var conv = await db.Conversations.FirstAsync(c => c.Id == convId, stoppingToken);

                        var message = new Message
                        {
                            ConversationId = convId,
                            Body = body,
                            Direction = MessageDirection.Incoming,
                            Status = MessageStatus.Sent,
                            SentAt = DateTime.UtcNow
                        };

                        db.Messages.Add(message);
                        conv.LastMessageAt = message.SentAt;
                        conv.UnreadCount += 1;

                        await db.SaveChangesAsync(stoppingToken);

                        await hub.Clients.All.SendAsync("ReceiveMessage", new
                        {
                            conversationId = conv.Id,
                            message = new
                            {
                                id = message.Id,
                                body = message.Body,
                                direction = (int)message.Direction,
                                sentAt = message.SentAt
                            },
                            unreadCount = conv.UnreadCount
                        }, stoppingToken);

                        _logger.LogInformation("[SIM] Injected fake message into conversation {Id}", conv.Id);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[SIM] Simulator tick failed");
                }

                await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            }
        }
    }
}
