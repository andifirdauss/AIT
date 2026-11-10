using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlanningTimeBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PlanningModelMs",
                table: "Threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlanningQueueMs",
                table: "Threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlanningToolMs",
                table: "Threads",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanningModelMs",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "PlanningQueueMs",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "PlanningToolMs",
                table: "Threads");
        }
    }
}
