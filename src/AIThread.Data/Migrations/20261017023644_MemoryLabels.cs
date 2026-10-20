using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemoryLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Area",
                table: "LongMemories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Keywords",
                table: "LongMemories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "LongMemories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LabelVersion",
                table: "LongMemories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Layers",
                table: "LongMemories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyLabel",
                table: "LongMemories",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "LongMemories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelabelAttempts",
                table: "LongMemories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "LongMemories",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CodeMapEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Layer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeMapEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModuleMergeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FromModule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IntoModule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Moved = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleMergeLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Aliases = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MergedInto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectModules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LongMemories_ProjectKey_Module",
                table: "LongMemories",
                columns: new[] { "ProjectKey", "Module" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeMapEntries_ProjectKey_Module",
                table: "CodeMapEntries",
                columns: new[] { "ProjectKey", "Module" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeMapEntries_ProjectKey_Path",
                table: "CodeMapEntries",
                columns: new[] { "ProjectKey", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleMergeLogs_ProjectKey_Status",
                table: "ModuleMergeLogs",
                columns: new[] { "ProjectKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectModules_ProjectKey_Name",
                table: "ProjectModules",
                columns: new[] { "ProjectKey", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeMapEntries");

            migrationBuilder.DropTable(
                name: "ModuleMergeLogs");

            migrationBuilder.DropTable(
                name: "ProjectModules");

            migrationBuilder.DropIndex(
                name: "IX_LongMemories_ProjectKey_Module",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "Area",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "Keywords",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "LabelVersion",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "Layers",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "LegacyLabel",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "RelabelAttempts",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "LongMemories");
        }
    }
}
