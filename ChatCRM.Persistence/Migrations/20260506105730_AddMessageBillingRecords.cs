using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 4 of the billing system. Adds the per-outgoing-message billing audit table.
    /// Pure additive — FKs to Messages (cascade) and WalletTransactions (set null) only.
    /// </remarks>
    public partial class AddMessageBillingRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageBillingRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    MetaCostUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ChargedUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    MarkupPercentageAtTime = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    WasInServiceWindow = table.Column<bool>(type: "bit", nullable: false),
                    FreeEntryPoint = table.Column<bool>(type: "bit", nullable: false),
                    AppliedRuleLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WalletTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageBillingRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageBillingRecords_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageBillingRecords_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageBillingRecords_MessageId",
                table: "MessageBillingRecords",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageBillingRecords_WalletTransactionId",
                table: "MessageBillingRecords",
                column: "WalletTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageBillingRecords_Category_CreatedAt",
                table: "MessageBillingRecords",
                columns: new[] { "Category", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MessageBillingRecords");
        }
    }
}
