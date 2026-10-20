using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlannerCachedTokens",
                table: "Threads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // The existing settings row gets the same default as a new one (SystemSetting).
            migrationBuilder.AddColumn<bool>(
                name: "PlannerCanAsk",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlannerCachedTokens",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "PlannerCanAsk",
                table: "SystemSettings");
        }
    }
}
