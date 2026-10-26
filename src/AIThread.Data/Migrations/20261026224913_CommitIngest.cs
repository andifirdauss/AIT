using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommitIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ingested",
                table: "ThreadChanges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CommitIngestSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Since = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxPerRun = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitIngestSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestedCommits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Commit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Author = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MemoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Digest = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    BusinessDigestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestedCommits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitIngestSettings_ProjectKey",
                table: "CommitIngestSettings",
                column: "ProjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestedCommits_ProjectKey_Commit",
                table: "IngestedCommits",
                columns: new[] { "ProjectKey", "Commit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestedCommits_ProjectKey_Status_BusinessDigestedAt",
                table: "IngestedCommits",
                columns: new[] { "ProjectKey", "Status", "BusinessDigestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommitIngestSettings");

            migrationBuilder.DropTable(
                name: "IngestedCommits");

            migrationBuilder.DropColumn(
                name: "Ingested",
                table: "ThreadChanges");
        }
    }
}
