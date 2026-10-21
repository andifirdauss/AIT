using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AutoTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TesterModel",
                table: "Threads",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TesterTokens",
                table: "Threads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TestingJson",
                table: "Threads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TestingMs",
                table: "Threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoTesting",
                table: "Nodes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TesterModel",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "TesterTokens",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "TestingJson",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "TestingMs",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "AutoTesting",
                table: "Nodes");
        }
    }
}
