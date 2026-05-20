using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 6 part 1. Adds the workspace-wide WhatsApp message template library.
    /// Pure additive — FK to WhatsAppInstances (set null) and AspNetUsers (set null).
    /// </remarks>
    public partial class AddWhatsAppTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Footer = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    MetaTemplateId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedViaInstanceId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppTemplates_WhatsAppInstances_SubmittedViaInstanceId",
                        column: x => x.SubmittedViaInstanceId,
                        principalTable: "WhatsAppInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WhatsAppTemplates_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_WorkspaceId_Name_LanguageCode",
                table: "WhatsAppTemplates",
                columns: new[] { "WorkspaceId", "Name", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_WorkspaceId_Status",
                table: "WhatsAppTemplates",
                columns: new[] { "WorkspaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_Status_SubmittedAt",
                table: "WhatsAppTemplates",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_SubmittedViaInstanceId",
                table: "WhatsAppTemplates",
                column: "SubmittedViaInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_CreatedByUserId",
                table: "WhatsAppTemplates",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WhatsAppTemplates");
        }
    }
}
