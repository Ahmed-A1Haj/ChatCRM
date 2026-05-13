using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Two additive changes:
    ///   1. <c>Agents.Instructions</c> — free-form system prompt / role definition.
    ///   2. <c>WhatsAppContacts.AssignedAgentId</c> — per-contact agent preference used by
    ///      auto-assignment when a new conversation is created.
    /// </remarks>
    public partial class AddAgentInstructionsAndContactAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedAgentId",
                table: "WhatsAppContacts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppContacts_AssignedAgentId",
                table: "WhatsAppContacts",
                column: "AssignedAgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppContacts_Agents_AssignedAgentId",
                table: "WhatsAppContacts",
                column: "AssignedAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppContacts_Agents_AssignedAgentId",
                table: "WhatsAppContacts");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppContacts_AssignedAgentId",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "AssignedAgentId",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "Agents");
        }
    }
}
