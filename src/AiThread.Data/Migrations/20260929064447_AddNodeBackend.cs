using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "Nodes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Router");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Backend",
                table: "Nodes");
        }
    }
}
