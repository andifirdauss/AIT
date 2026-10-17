using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemoryExtraModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraModules",
                table: "LongMemories",
                type: "nvarchar(310)",
                maxLength: 310,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraModules",
                table: "LongMemories");
        }
    }
}
