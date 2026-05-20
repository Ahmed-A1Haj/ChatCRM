using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 2 of the billing system. Adds the append-only ledger (WalletTransactions) and the
    /// billing audit log (BillingAuditLogs). Pure additive — no changes to existing tables.
    /// FKs to Wallets, Messages, AspNetUsers all use SetNull/Restrict so deleting a User or a
    /// Message does NOT cascade-delete their financial history.
    /// </remarks>
    public partial class AddWalletTransactionsAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    AmountUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BalanceAfterUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MessageId = table.Column<int>(type: "int", nullable: true),
                    InitiatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_AspNetUsers_InitiatedByUserId",
                        column: x => x.InitiatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_CreatedAt",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_MessageId",
                table: "WalletTransactions",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_InitiatedByUserId",
                table: "WalletTransactions",
                column: "InitiatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Reference",
                table: "WalletTransactions",
                column: "Reference");

            migrationBuilder.CreateTable(
                name: "BillingAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAuditLogs_AtUtc",
                table: "BillingAuditLogs",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAuditLogs_EntityType_EntityId",
                table: "BillingAuditLogs",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BillingAuditLogs");
            migrationBuilder.DropTable(name: "WalletTransactions");
        }
    }
}
