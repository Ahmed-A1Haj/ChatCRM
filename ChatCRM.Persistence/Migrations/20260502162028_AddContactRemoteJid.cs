using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactRemoteJid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteJid",
                table: "WhatsAppContacts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteJid",
                table: "WhatsAppContacts");
        }
    }
}
