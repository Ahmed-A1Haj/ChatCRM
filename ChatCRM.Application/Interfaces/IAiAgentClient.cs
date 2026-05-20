using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatCRM.Application.Ai.DTOs;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Producer side of the ChatCRM ↔ CRM-AI-Service bridge. Today it only enqueues
    /// <c>create_agent</c> messages (the only intent the upstream AI service handles
    /// natively). Each call inserts a row in <c>AiOutboxMessages</c>; a background
    /// publisher drains that table to Redis Streams via XADD. The two-phase shape
    /// (DB row first, network send later) makes the bridge crash-safe — if the
    /// process dies between the business save and the network publish, the
    /// publisher will re-emit on restart.
    /// </summary>
    public interface IAiAgentClient
    {
        /// <summary>
        /// Enqueue a <c>create_agent</c> message: tells the AI service to materialize
        /// a remote agent row with this system prompt and (optionally) knowledge base.
        /// The remote UUID flows back via the outbound stream and is stored on
        /// <c>Agents.RemoteAgentId</c>.
        /// </summary>
        Task EnqueueCreateAgentAsync(
            int localAgentId,
            string instruction,
            IReadOnlyList<AiFileRef> files,
            IReadOnlyList<AiLinkRef> links,
            CancellationToken cancellationToken = default);
    }
}
