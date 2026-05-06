using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 5 of the billing roadmap (sequenced before phase 4 because phase 4 needs the
    /// service-window state to pick a pricing category). Adds a denormalized
    /// <c>LastIncomingAt</c> column on Conversations so the 24-hour customer-service-window
    /// check is a single column read instead of a MAX(SentAt) per render.
    ///
    /// Backfill is idempotent: it sets <c>LastIncomingAt</c> to the most recent
    /// <c>Message.SentAt</c> where Direction == 0 (Incoming). Conversations with no incoming
    /// messages stay NULL (we initiated and the contact never replied).
    /// </remarks>
    public partial class AddConversationLastIncomingAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastIncomingAt",
                table: "Conversations",
                type: "datetime2",
                nullable: true);

            // Backfill from existing message history — one-shot per conversation.
            migrationBuilder.Sql(@"
                UPDATE c
                SET    c.LastIncomingAt = m.MaxSentAt
                FROM   Conversations c
                INNER JOIN (
                    SELECT ConversationId, MAX(SentAt) AS MaxSentAt
                    FROM   Messages
                    WHERE  Direction = 0   -- Incoming
                    GROUP BY ConversationId
                ) m ON m.ConversationId = c.Id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastIncomingAt", table: "Conversations");
        }
    }
}
