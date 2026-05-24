using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "WhatsAppContacts",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "WhatsAppContacts",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "WhatsAppContacts",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "WhatsAppContacts",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WhatsAppContacts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Company",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "WhatsAppContacts");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WhatsAppContacts");
        }
    }
}
