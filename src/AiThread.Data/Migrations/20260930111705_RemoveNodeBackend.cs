using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNodeBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Planner nodes that ran through the Claude Code CLI hold a CLI model alias ("opus"), not a 9router model.
            // They are switched off rather than deleted, so nothing is lost and the user can remove or re-point them.
            migrationBuilder.Sql("UPDATE Nodes SET IsActive = 0 WHERE Backend = 'ClaudeCli'");

            migrationBuilder.DropColumn(
                name: "Backend",
                table: "Nodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "Nodes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Router");
        }
    }
}
