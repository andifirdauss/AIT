using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommandTimeoutSeconds",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<bool>(
                name: "CommandToolEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "GitPublishEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ApprovalGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalGrants_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalGrants_ProjectId_Kind_Target",
                table: "ApprovalGrants",
                columns: new[] { "ProjectId", "Kind", "Target" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalGrants");

            migrationBuilder.DropColumn(
                name: "CommandTimeoutSeconds",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CommandToolEnabled",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "GitPublishEnabled",
                table: "SystemSettings");
        }
    }
}
