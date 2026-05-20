using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the WhatsAppInstances table first so we can reference it.
            migrationBuilder.CreateTable(
                name: "WhatsAppInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OwnerJid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppInstances_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // 2. Seed a default instance row for existing conversations to attach to.
            //    Uses "chatcrm" as the Evolution instance name matching the original single-instance setup.
            migrationBuilder.Sql(@"
                INSERT INTO WhatsAppInstances (InstanceName, DisplayName, Status, CreatedAt)
                VALUES ('chatcrm', 'Main Line', 2, SYSUTCDATETIME());
            ");

            // 3. Drop the old per-contact index before restructuring.
            migrationBuilder.DropIndex(
                name: "IX_Conversations_ContactId",
                table: "Conversations");

            // 4. Add the FK column with a default of 0 (no existing rows match it — we fix that in step 5).
            migrationBuilder.AddColumn<int>(
                name: "WhatsAppInstanceId",
                table: "Conversations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // 5. Backfill every existing conversation to point at the seeded default instance.
            migrationBuilder.Sql(@"
                UPDATE Conversations
                SET WhatsAppInstanceId = (SELECT TOP 1 Id FROM WhatsAppInstances WHERE InstanceName = 'chatcrm');
            ");

            // 6. Now the data is consistent, apply the unique index.
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ContactId_WhatsAppInstanceId",
                table: "Conversations",
                columns: new[] { "ContactId", "WhatsAppInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WhatsAppInstanceId_LastMessageAt",
                table: "Conversations",
                columns: new[] { "WhatsAppInstanceId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInstances_CreatedByUserId",
                table: "WhatsAppInstances",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInstances_InstanceName",
                table: "WhatsAppInstances",
                column: "InstanceName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_WhatsAppInstances_WhatsAppInstanceId",
                table: "Conversations",
                column: "WhatsAppInstanceId",
                principalTable: "WhatsAppInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_WhatsAppInstances_WhatsAppInstanceId",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "WhatsAppInstances");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ContactId_WhatsAppInstanceId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_WhatsAppInstanceId_LastMessageAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WhatsAppInstanceId",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ContactId",
                table: "Conversations",
                column: "ContactId");
        }
    }
}
