using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatCRM.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Removes the unused <c>Agents.Model</c> column. The original AddAgents migration created
    /// it as NOT NULL; this migration drops it so the entity (which no longer carries Model)
    /// can insert rows without supplying a value. Drop is straightforward — no data we need
    /// to preserve since model selection wasn't actually wired to any inference logic.
    /// </remarks>
    public partial class DropAgentModelColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "Agents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Agents",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");
        }
    }
}
