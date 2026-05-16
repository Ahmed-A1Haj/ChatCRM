using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatCRM.Application.Ai.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Ai
{
    /// <summary>
    /// Default <see cref="IAiAgentClient"/> implementation. Writes a row to
    /// <c>AiOutboxMessages</c>; the actual XADD happens in
    /// <see cref="AiOutboxPublisher"/>. Caller is expected to <c>SaveChangesAsync</c> in
    /// the same business transaction so business state and outbox row commit atomically.
    /// </summary>
    public sealed class AiAgentClient : IAiAgentClient
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly AppDbContext _db;
        private readonly AiOptions _options;

        public AiAgentClient(AppDbContext db, IOptions<AiOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public Task EnqueueCreateAgentAsync(
            int localAgentId,
            string instruction,
            IReadOnlyList<AiFileRef> files,
            IReadOnlyList<AiLinkRef> links,
            CancellationToken cancellationToken = default)
        {
            // Stream fields are stored as plain strings; JSON-encode lists so the AI
            // service's pydantic field_validator can decode them back into models.
            var payload = new Dictionary<string, string>
            {
                ["tenant_id"]   = _options.TenantId,
                ["instruction"] = instruction ?? string.Empty,
            };
            if (files is { Count: > 0 })
                payload["files"] = JsonSerializer.Serialize(files, JsonOpts);
            if (links is { Count: > 0 })
                payload["links"] = JsonSerializer.Serialize(links, JsonOpts);

            // local_agent_id is round-tripped through the outbox row PayloadJson so the
            // AiOutboundConsumer can match the agent_created reply back to its source
            // Agents row. The AI service ignores fields it doesn't know about, so we
            // can ship this without touching the upstream schema.
            payload["local_agent_id"] = localAgentId.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

            _db.AiOutboxMessages.Add(new AiOutboxMessage
            {
                Kind = "create_agent",
                PayloadJson = JsonSerializer.Serialize(payload, JsonOpts),
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
            });
            return Task.CompletedTask;
        }
    }
}
