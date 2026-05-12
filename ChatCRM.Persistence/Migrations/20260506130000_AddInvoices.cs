using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>Phase 9. Workspace-monthly invoice records. Pure additive.</remarks>
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TotalChargedUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalMetaCostUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalToppedUpUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PaidMessageCount = table.Column<int>(type: "int", nullable: false),
                    FreeMessageCount = table.Column<int>(type: "int", nullable: false),
                    CategoryBreakdownJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IssuedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_AspNetUsers_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WorkspaceId_Number",
                table: "Invoices",
                columns: new[] { "WorkspaceId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WorkspaceId_PeriodStartUtc",
                table: "Invoices",
                columns: new[] { "WorkspaceId", "PeriodStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedByUserId",
                table: "Invoices",
                column: "IssuedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Invoices");
        }
    }
}
