using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 0 of the billing system. Adds the Wallets table only — no foreign keys to
    /// existing tables, no changes to any existing column. Safe to run on a populated DB:
    /// existing rows are untouched and the seeder inserts the singleton wallet row at startup
    /// (see BillingSeeder).
    /// </remarks>
    public partial class AddWalletsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    BalanceUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    DisplayCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    LowBalanceThresholdUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 10m),
                    AutoRechargeEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AutoRechargeAmountUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    AutoRechargeTriggerUsd = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    StripeCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DefaultPaymentMethodId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            // Unique index enforces the v1 singleton-per-workspace invariant at the DB level.
            // When multi-tenancy ships, this index stays valid (still one wallet per workspace).
            migrationBuilder.CreateIndex(
                name: "IX_Wallets_WorkspaceId",
                table: "Wallets",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Wallets");
        }
    }
}
