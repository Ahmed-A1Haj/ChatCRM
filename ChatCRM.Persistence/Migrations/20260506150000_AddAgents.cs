using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// AI Agents feature. Adds the workspace-scoped <c>Agents</c> table plus
    /// <c>Conversations.AssignedAgentId</c> for auto- and manual-assignment.
    /// Filtered unique index enforces the "at most one default per workspace" invariant.
    /// </remarks>
    public partial class AddAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AvatarPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_WorkspaceId_Name",
                table: "Agents",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            // Filtered unique index — at most one IsDefault=1 per workspace. SQL Server only.
            migrationBuilder.CreateIndex(
                name: "IX_Agents_WorkspaceId_DefaultUnique",
                table: "Agents",
                column: "WorkspaceId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_WorkspaceId_IsActive",
                table: "Agents",
                columns: new[] { "WorkspaceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_CreatedByUserId",
                table: "Agents",
                column: "CreatedByUserId");

            // Add the per-conversation FK to the agent.
            migrationBuilder.AddColumn<int>(
                name: "AssignedAgentId",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_AssignedAgentId",
                table: "Conversations",
                column: "AssignedAgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Agents_AssignedAgentId",
                table: "Conversations",
                column: "AssignedAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Agents_AssignedAgentId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_AssignedAgentId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AssignedAgentId",
                table: "Conversations");

            migrationBuilder.DropTable(name: "Agents");
        }
    }
}
