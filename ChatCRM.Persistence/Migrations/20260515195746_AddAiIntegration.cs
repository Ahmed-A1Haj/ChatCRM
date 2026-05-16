using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Three additive changes for the CRM-AI-Service integration:
    ///   1. <c>Agents.RemoteAgentId / RemoteSyncStatus / RemoteSyncedAt / RemoteSyncError</c> —
    ///      bridge to the remote agent UUID in CRM-AI-Service (Postgres + pgvector).
    ///   2. <c>Messages.AuthorAgentId</c> — distinguishes AI-authored outgoing messages from
    ///      human-authored ones (the existing <c>AuthorUserId</c> only handled humans).
    ///   3. <c>AiOutboxMessages</c> table — transactional outbox for Redis Stream publishes.
    ///      Survives crashes between business save and XADD.
    /// </remarks>
    public partial class AddAiIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorAgentId",
                table: "Messages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemoteAgentId",
                table: "Agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteSyncError",
                table: "Agents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteSyncStatus",
                table: "Agents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteSyncedAt",
                table: "Agents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StreamId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AuthorAgentId",
                table: "Messages",
                column: "AuthorAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RemoteAgentId",
                table: "Agents",
                column: "RemoteAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiOutboxMessages_Status_CreatedAt",
                table: "AiOutboxMessages",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Agents_AuthorAgentId",
                table: "Messages",
                column: "AuthorAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Agents_AuthorAgentId",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "AiOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_AuthorAgentId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Agents_RemoteAgentId",
                table: "Agents");

            migrationBuilder.DropColumn(name: "AuthorAgentId", table: "Messages");
            migrationBuilder.DropColumn(name: "RemoteAgentId", table: "Agents");
            migrationBuilder.DropColumn(name: "RemoteSyncError", table: "Agents");
            migrationBuilder.DropColumn(name: "RemoteSyncStatus", table: "Agents");
            migrationBuilder.DropColumn(name: "RemoteSyncedAt", table: "Agents");
        }
    }
}
