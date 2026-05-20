using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInstanceIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 == WhatsAppIntegration.Personal — every existing instance was a Baileys QR scan,
            // so backfilling with Personal is correct.
            migrationBuilder.AddColumn<byte>(
                name: "Integration",
                table: "WhatsAppInstances",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Integration",
                table: "WhatsAppInstances");
        }
    }
}
