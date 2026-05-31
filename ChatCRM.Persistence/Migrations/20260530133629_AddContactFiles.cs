using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContactId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactFiles_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ContactFiles_WhatsAppContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "WhatsAppContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactFiles_ContactId",
                table: "ContactFiles",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactFiles_UploadedByUserId",
                table: "ContactFiles",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactFiles");
        }
    }
}
