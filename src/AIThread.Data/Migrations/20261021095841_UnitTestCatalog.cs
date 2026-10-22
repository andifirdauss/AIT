using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnitTestCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitTestJson",
                table: "Threads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UnitTestMs",
                table: "Threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnitTestCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Scenario = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Layers = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Keywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TargetFiles = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TargetSymbols = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Framework = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TestProject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TestFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MethodName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Selector = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Requires = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObsoleteReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ObsoleteThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObsoleteBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ObsoleteAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuarantineReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RemovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastResult = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    LastMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastSkipReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastInputHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceCommit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MethodSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupportFiles = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTestCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitTestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitTestCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Phase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Machine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RunBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitTestSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Authoring = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    KeepSnapshots = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitTestSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTestCases_ProjectKey_Number",
                table: "UnitTestCases",
                columns: new[] { "ProjectKey", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitTestCases_ProjectKey_TestFile",
                table: "UnitTestCases",
                columns: new[] { "ProjectKey", "TestFile" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTestRuns_ProjectKey",
                table: "UnitTestRuns",
                column: "ProjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_UnitTestRuns_UnitTestCaseId_CreatedAt",
                table: "UnitTestRuns",
                columns: new[] { "UnitTestCaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitTestSettings_ProjectKey",
                table: "UnitTestSettings",
                column: "ProjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnitTestCases");

            migrationBuilder.DropTable(
                name: "UnitTestRuns");

            migrationBuilder.DropTable(
                name: "UnitTestSettings");

            migrationBuilder.DropColumn(
                name: "UnitTestJson",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "UnitTestMs",
                table: "Threads");
        }
    }
}
