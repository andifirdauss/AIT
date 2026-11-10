using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class SilentNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SilentNotifications",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SilentNotifications",
                table: "SystemSettings");
        }
    }
}
