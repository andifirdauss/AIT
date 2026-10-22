using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class TestingScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestingScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Steps = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Roles = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fields = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceFollowUp = table.Column<int>(type: "int", nullable: true),
                    LastRun = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestingScenarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestingScenarios_ProjectKey_Slug",
                table: "TestingScenarios",
                columns: new[] { "ProjectKey", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestingScenarios");
        }
    }
}
