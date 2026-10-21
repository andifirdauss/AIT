using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvestigationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvestigationJson",
                table: "Threads",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvestigationJson",
                table: "Threads");
        }
    }
}
