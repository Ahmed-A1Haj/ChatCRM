using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Phase 1 of the billing system. Adds two purely-additive tables — MetaPricingRules
    /// (versioned per category × country) and BillingSettings (per-workspace singleton).
    /// No FK to existing tables. Safe to run on populated DBs.
    /// </remarks>
    public partial class AddBillingPricingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetaPricingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<byte>(type: "tinyint", nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BasePriceUsd = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaPricingRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetaPricingRules_Category_CountryCode_EffectiveFrom",
                table: "MetaPricingRules",
                columns: new[] { "Category", "CountryCode", "EffectiveFrom" });

            migrationBuilder.CreateTable(
                name: "BillingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    MarkupPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 35m),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    RefundPolicy = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    InvoiceFooter = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSettings_WorkspaceId",
                table: "BillingSettings",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BillingSettings");
            migrationBuilder.DropTable(name: "MetaPricingRules");
        }
    }
}
