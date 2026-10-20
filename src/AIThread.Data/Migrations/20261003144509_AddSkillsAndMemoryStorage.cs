using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillsAndMemoryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing settings rows get the same defaults as a new one (SystemSetting, MemoryStoreSetting).
            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_Database",
                table: "SystemSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_Host",
                table: "SystemSettings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_Options",
                table: "SystemSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_PasswordProtected",
                table: "SystemSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LongMemoryStore_Port",
                table: "SystemSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_Provider",
                table: "SystemSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<string>(
                name: "LongMemoryStore_Username",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_Database",
                table: "SystemSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_Host",
                table: "SystemSettings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_Options",
                table: "SystemSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_PasswordProtected",
                table: "SystemSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShortMemoryStore_Port",
                table: "SystemSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_Provider",
                table: "SystemSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<string>(
                name: "ShortMemoryStore_Username",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkillOrchestratorModel",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkillPromptMaxChars",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 40000);

            migrationBuilder.CreateTable(
                name: "SkillSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastPulledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SkillCount = table.Column<int>(type: "int", nullable: false),
                    ParentSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkillSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelativeDir = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EntryFile = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Skills_SkillSources_SkillSourceId",
                        column: x => x.SkillSourceId,
                        principalTable: "SkillSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeSkills",
                columns: table => new
                {
                    AiNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeSkills", x => new { x.AiNodeId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_NodeSkills_Nodes_AiNodeId",
                        column: x => x.AiNodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NodeSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeSkills_SkillId",
                table: "NodeSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_SkillSourceId_RelativeDir_EntryFile",
                table: "Skills",
                columns: new[] { "SkillSourceId", "RelativeDir", "EntryFile" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillSources_Url",
                table: "SkillSources",
                column: "Url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeSkills");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "SkillSources");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Database",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Host",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Options",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_PasswordProtected",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Port",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Provider",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LongMemoryStore_Username",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Database",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Host",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Options",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_PasswordProtected",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Port",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Provider",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ShortMemoryStore_Username",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SkillOrchestratorModel",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SkillPromptMaxChars",
                table: "SystemSettings");
        }
    }
}
