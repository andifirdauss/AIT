using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class GitTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitAuthor",
                table: "Threads",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitBaseCommit",
                table: "Threads",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitBranch",
                table: "Threads",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GitCheckedAt",
                table: "Threads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitCommits",
                table: "Threads",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitState",
                table: "Threads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterBlob",
                table: "ThreadFileSnapshots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitCommit",
                table: "ThreadFileSnapshots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitState",
                table: "ThreadFileSnapshots",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitAuthor",
                table: "LongMemories",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitBranch",
                table: "LongMemories",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitCommit",
                table: "LongMemories",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitAuthor",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "GitBaseCommit",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "GitBranch",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "GitCheckedAt",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "GitCommits",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "GitState",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "AfterBlob",
                table: "ThreadFileSnapshots");

            migrationBuilder.DropColumn(
                name: "GitCommit",
                table: "ThreadFileSnapshots");

            migrationBuilder.DropColumn(
                name: "GitState",
                table: "ThreadFileSnapshots");

            migrationBuilder.DropColumn(
                name: "GitAuthor",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "GitBranch",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "GitCommit",
                table: "LongMemories");
        }
    }
}
