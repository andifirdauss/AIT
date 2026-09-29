using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelegramBotTokenProtected",
                table: "SystemSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelegramEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TelegramChats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    ActiveProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversationStartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramChats_Projects_ActiveProjectId",
                        column: x => x.ActiveProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChats_ActiveProjectId",
                table: "TelegramChats",
                column: "ActiveProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChats_ChatId",
                table: "TelegramChats",
                column: "ChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramChats");

            migrationBuilder.DropColumn(
                name: "TelegramBotTokenProtected",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "TelegramEnabled",
                table: "SystemSettings");
        }
    }
}
