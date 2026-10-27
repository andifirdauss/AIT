using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class GitTrackingShared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GitEdited",
                table: "ThreadFileSnapshots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GitFetchMinutes",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ThreadChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GitAuthor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GitBranch = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    GitBaseCommit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Commits = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Files = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThreadChanges_ProjectKey",
                table: "ThreadChanges",
                column: "ProjectKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThreadChanges");

            migrationBuilder.DropColumn(
                name: "GitEdited",
                table: "ThreadFileSnapshots");

            migrationBuilder.DropColumn(
                name: "GitFetchMinutes",
                table: "Projects");
        }
    }
}
