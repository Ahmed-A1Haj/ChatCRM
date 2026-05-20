namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Transactional outbox row for messages destined for the CRM-AI-Service over Redis Streams.
    /// Writers (AgentService, EvolutionService webhook handler) insert rows in the same
    /// SaveChangesAsync as the business mutation that triggered them. A background publisher
    /// drains <c>Status='pending'</c> rows to <c>stream:inbound</c> via <c>XADD</c> and marks
    /// them <c>sent</c>. Survives crashes between business save and the network publish — the
    /// publisher will re-emit anything left in <c>pending</c> on restart.
    /// </summary>
    public class AiOutboxMessage
    {
        public long Id { get; set; }

        /// <summary>Discriminator field copied verbatim into the Redis Stream message. e.g. <c>create_agent</c> or <c>chat_message</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Serialized message body — a JSON object whose keys/values become Redis Stream fields.</summary>
        public string PayloadJson { get; set; } = string.Empty;

        /// <summary><c>pending</c> | <c>sent</c> | <c>error</c>.</summary>
        public string Status { get; set; } = "pending";

        public int Attempts { get; set; }

        public string? LastError { get; set; }

        /// <summary>Redis-assigned stream entry ID after a successful XADD.</summary>
        public string? StreamId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? SentAt { get; set; }
    }
}
