using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTranscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TranscriptionAttemptCount",
                table: "Messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionError",
                table: "Messages",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionLanguage",
                table: "Messages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TranscriptionNextAttemptUtc",
                table: "Messages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionProvider",
                table: "Messages",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "TranscriptionStatus",
                table: "Messages",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionText",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_TranscriptionStatus",
                table: "Messages",
                column: "TranscriptionStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_TranscriptionStatus",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionAttemptCount",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionError",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionLanguage",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionNextAttemptUtc",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionProvider",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionStatus",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "TranscriptionText",
                table: "Messages");
        }
    }
}
