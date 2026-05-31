using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatCRM.Infrastructure.Services.Ai
{
    /// <summary>
    /// Subscribes to <c>stream:outbound</c> via XREADGROUP and dispatches each entry to
    /// <see cref="IAiReplyDispatcher"/>.
    ///
    /// Upstream CRM-AI-Service emits replies with this shape (see its OutboundMessage
    /// schema):
    ///   { tenant_id, agent_id, status: "created" | "failed", error?, files_processed?, ... }
    /// We branch on <c>status</c> because the upstream schema doesn't carry a discriminator
    /// field. Creates a fresh DI scope per entry so the dispatcher gets its own DbContext.
    /// </summary>
    public sealed class AiOutboundConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConnectionMultiplexer _redis;
        private readonly AiOptions _options;
        private readonly ILogger<AiOutboundConsumer> _logger;

        public AiOutboundConsumer(
            IServiceScopeFactory scopeFactory,
            IConnectionMultiplexer redis,
            IOptions<AiOptions> options,
            ILogger<AiOutboundConsumer> logger)
        {
            _scopeFactory = scopeFactory;
            _redis = redis;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await EnsureGroupAsync();

            _logger.LogInformation(
                "[AI-OUTBOX] consumer started — stream={Stream} group={Group} name={Name}",
                _options.OutboundStream, _options.ConsumerGroup, _options.ConsumerName);

            var db = _redis.GetDatabase();

            while (!stoppingToken.IsCancellationRequested)
            {
                StreamEntry[] entries;
                try
                {
                    entries = await db.StreamReadGroupAsync(
                        _options.OutboundStream,
                        _options.ConsumerGroup,
                        _options.ConsumerName,
                        position: ">",
                        count: 16);
                }
                catch (RedisConnectionException ex)
                {
                    _logger.LogWarning(ex, "[AI-OUTBOX] redis connection issue — retrying in 5s");
                    try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { }
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI-OUTBOX] xreadgroup threw");
                    try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { }
                    continue;
                }

                if (entries.Length == 0)
                {
                    // StackExchange.Redis doesn't expose BLOCK semantics for streams; poll briefly.
                    try { await Task.Delay(500, stoppingToken); } catch (OperationCanceledException) { }
                    continue;
                }

                foreach (var entry in entries)
                {
                    try
                    {
                        await DispatchAsync(entry, stoppingToken);
                        await db.StreamAcknowledgeAsync(
                            _options.OutboundStream, _options.ConsumerGroup, entry.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[AI-OUTBOX] dispatch failed for {Id}; leaving in PEL", entry.Id);
                    }
                }
            }

            _logger.LogInformation("[AI-OUTBOX] consumer stopped");
        }

        private async Task EnsureGroupAsync()
        {
            var db = _redis.GetDatabase();
            try
            {
                await db.StreamCreateConsumerGroupAsync(
                    _options.OutboundStream, _options.ConsumerGroup, position: "$", createStream: true);
                _logger.LogInformation(
                    "[AI-OUTBOX] created consumer group {Group} on {Stream}",
                    _options.ConsumerGroup, _options.OutboundStream);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // already exists — fine.
            }
            catch (RedisConnectionException ex)
            {
                // Redis isn't reachable (AI is optional). Don't let this crash the host — the
                // read loop catches the same exception and retries, recreating the group later.
                _logger.LogWarning(ex, "[AI-OUTBOX] redis unavailable at startup — group creation deferred");
            }
        }

        private async Task DispatchAsync(StreamEntry entry, CancellationToken cancellationToken)
        {
            string? Get(string name)
            {
                foreach (var f in entry.Values)
                    if (f.Name == name) return f.Value.ToString();
                return null;
            }

            var status   = Get("status");
            var tenantId = Get("tenant_id") ?? string.Empty;

            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IAiReplyDispatcher>();

            switch (status)
            {
                case "created":
                    {
                        var remoteStr = Get("agent_id");
                        if (!Guid.TryParse(remoteStr, out var remote))
                        {
                            _logger.LogWarning(
                                "[AI-OUTBOX] created reply without valid agent_id (got '{V}')", remoteStr);
                            return;
                        }
                        await dispatcher.HandleAgentCreatedAsync(tenantId, remote, cancellationToken);
                        break;
                    }

                case "failed":
                    {
                        await dispatcher.HandleFailedAsync(tenantId, Get("error"), cancellationToken);
                        break;
                    }

                default:
                    _logger.LogWarning(
                        "[AI-OUTBOX] unknown status '{Status}' on entry {Id}", status, entry.Id);
                    break;
            }
        }
    }
}
