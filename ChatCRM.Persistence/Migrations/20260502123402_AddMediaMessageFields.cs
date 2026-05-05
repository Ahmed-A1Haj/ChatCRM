using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaMessageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                table: "Messages",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "MediaFileName",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaMimeType",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaUrl",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaFileName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaMimeType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "MediaUrl",
                table: "Messages");
        }
    }
}
