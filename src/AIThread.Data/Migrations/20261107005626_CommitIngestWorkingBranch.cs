using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class CommitIngestWorkingBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "IngestedCommits",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatchId",
                table: "IngestedCommits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreWorkingBranch",
                table: "CommitIngestSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Branch",
                table: "IngestedCommits");

            migrationBuilder.DropColumn(
                name: "PatchId",
                table: "IngestedCommits");

            migrationBuilder.DropColumn(
                name: "IgnoreWorkingBranch",
                table: "CommitIngestSettings");
        }
    }
}
