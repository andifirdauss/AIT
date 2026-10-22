using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class TestAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DevPassword = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DevPasswordSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Impersonation = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    SideEffectsAcknowledgedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SideEffectsAcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAccounts_ProjectKey_Username",
                table: "TestAccounts",
                columns: new[] { "ProjectKey", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestingSettings_ProjectKey",
                table: "TestingSettings",
                column: "ProjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAccounts");

            migrationBuilder.DropTable(
                name: "TestingSettings");
        }
    }
}
