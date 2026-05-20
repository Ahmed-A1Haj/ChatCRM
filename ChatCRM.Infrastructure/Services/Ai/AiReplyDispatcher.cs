using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Ai
{
    /// <summary>
    /// Handles messages read off <c>stream:outbound</c>. Today the upstream AI service
    /// only emits the create-agent ack (<c>status=created</c> + <c>agent_id</c> UUID).
    ///
    /// Correlation strategy: Redis Streams + a single AI-worker consumer preserve
    /// in-order delivery, so the i-th reply matches the i-th inbound XADD. We track
    /// "in-flight" XADDs via the <c>AiOutboxMessages</c> table — each row enters
    /// <c>Status='sent'</c> when published, and the oldest <c>sent</c> row is the
    /// one this reply belongs to. We use its <c>local_agent_id</c> (stored in
    /// <c>PayloadJson</c>) to find the ChatCRM Agent row and stamp it synced, then
    /// flip the outbox row to <c>Status='completed'</c>. This makes the correlation
    /// robust against orphan Agent rows from before the integration existed.
    /// </summary>
    public interface IAiReplyDispatcher
    {
        Task HandleAgentCreatedAsync(
            string tenantId,
            Guid remoteAgentId,
            CancellationToken cancellationToken);

        Task HandleFailedAsync(
            string tenantId,
            string? error,
            CancellationToken cancellationToken);
    }

    public sealed class AiReplyDispatcher : IAiReplyDispatcher
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AiReplyDispatcher> _logger;

        public AiReplyDispatcher(AppDbContext db, ILogger<AiReplyDispatcher> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task HandleAgentCreatedAsync(
            string tenantId, Guid remoteAgentId, CancellationToken ct)
        {
            var row = await NextInFlightAsync("create_agent", ct);
            if (row is null)
            {
                _logger.LogWarning(
                    "[AI-SYNC] agent_created (remote={Remote}) but no in-flight outbox row to correlate — ignoring",
                    remoteAgentId);
                return;
            }

            var localAgentId = TryReadLocalAgentId(row.PayloadJson);
            if (localAgentId is null)
            {
                _logger.LogWarning(
                    "[AI-SYNC] outbox row {Row} has no local_agent_id — marking the row error",
                    row.Id);
                row.Status = "error";
                row.LastError = "missing_local_agent_id";
                await _db.SaveChangesAsync(ct);
                return;
            }

            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == localAgentId.Value, ct);
            if (agent is null)
            {
                _logger.LogWarning(
                    "[AI-SYNC] Agent {Local} referenced by outbox {Row} no longer exists — discarding sync",
                    localAgentId, row.Id);
                row.Status = "completed";
                await _db.SaveChangesAsync(ct);
                return;
            }

            agent.RemoteAgentId = remoteAgentId;
            agent.RemoteSyncStatus = "synced";
            agent.RemoteSyncedAt = DateTime.UtcNow;
            agent.RemoteSyncError = null;
            agent.UpdatedAt = DateTime.UtcNow;

            row.Status = "completed";
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AI-SYNC] Agent {Local} '{Name}' synced to remote {Remote}",
                agent.Id, agent.Name, remoteAgentId);
        }

        public async Task HandleFailedAsync(string tenantId, string? error, CancellationToken ct)
        {
            var row = await NextInFlightAsync("create_agent", ct);
            if (row is null)
            {
                _logger.LogWarning(
                    "[AI-FAIL] failed reply but no in-flight outbox row to attribute (error={Error})",
                    error);
                return;
            }

            row.Status = "completed"; // we processed the reply, even if the outcome was an error
            row.LastError = error;

            var localAgentId = TryReadLocalAgentId(row.PayloadJson);
            if (localAgentId is not null)
            {
                var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == localAgentId.Value, ct);
                if (agent is not null)
                {
                    agent.RemoteSyncStatus = "error";
                    agent.RemoteSyncError = error?.Length > 500 ? error[..500] : error;
                    agent.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "[AI-FAIL] Outbox row {Row} (agent={Local}) marked error: {Error}",
                row.Id, localAgentId, error);
        }

        private Task<Domain.Entities.AiOutboxMessage?> NextInFlightAsync(string kind, CancellationToken ct)
        {
            // FIFO across all sent rows of this kind. SentAt ordering matches the order
            // we XADD'd them, which matches the order replies arrive.
            return _db.AiOutboxMessages
                .Where(r => r.Status == "sent" && r.Kind == kind)
                .OrderBy(r => r.SentAt)
                .ThenBy(r => r.Id)
                .FirstOrDefaultAsync(ct);
        }

        private static int? TryReadLocalAgentId(string payloadJson)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson);
                if (dict is not null && dict.TryGetValue("local_agent_id", out var s)
                    && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    return v;
                }
            }
            catch (JsonException) { }
            return null;
        }
    }
}
