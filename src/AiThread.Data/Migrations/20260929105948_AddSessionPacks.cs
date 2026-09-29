using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionPacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkingDirectory = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Pack = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousPackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HandedToSessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HandedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionPacks_HandedToSessionId",
                table: "SessionPacks",
                column: "HandedToSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPacks_WorkingDirectory_CreatedAt",
                table: "SessionPacks",
                columns: new[] { "WorkingDirectory", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionPacks");
        }
    }
}
