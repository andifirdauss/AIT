using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectKeyAndBusinessMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BusinessDigestedAt",
                table: "Threads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BusinessMemoryEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessMemoryIntervalHours",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryModel",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_Database",
                table: "SystemSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_Host",
                table: "SystemSettings",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_Options",
                table: "SystemSettings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_PasswordProtected",
                table: "SystemSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessMemoryStore_Port",
                table: "SystemSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_Provider",
                table: "SystemSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Default");

            migrationBuilder.AddColumn<string>(
                name: "BusinessMemoryStore_Username",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeveloperName",
                table: "SystemSettings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualKey",
                table: "Projects",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectKey",
                table: "Projects",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProjectKeySource",
                table: "Projects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "ProjectKey",
                table: "LongMemories",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                table: "LongMemories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MediaType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SourceThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPageRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Author = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EditKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceThreadIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPageRevisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Labels = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AutoUpdate = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastEditKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThreadAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThreadRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreadAttachments_Threads_ThreadRunId",
                        column: x => x.ThreadRunId,
                        principalTable: "Threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Threads_Status_BusinessDigestedAt",
                table: "Threads",
                columns: new[] { "Status", "BusinessDigestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectKey",
                table: "Projects",
                column: "ProjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_LongMemories_ProjectKey_Type",
                table: "LongMemories",
                columns: new[] { "ProjectKey", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAssets_PageId",
                table: "BusinessAssets",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAssets_ProjectKey_Sha256",
                table: "BusinessAssets",
                columns: new[] { "ProjectKey", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPageRevisions_PageId_Version",
                table: "BusinessPageRevisions",
                columns: new[] { "PageId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPages_ProjectKey_Slug",
                table: "BusinessPages",
                columns: new[] { "ProjectKey", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThreadAttachments_ThreadRunId",
                table: "ThreadAttachments",
                column: "ThreadRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessAssets");

            migrationBuilder.DropTable(
                name: "BusinessPageRevisions");

            migrationBuilder.DropTable(
                name: "BusinessPages");

            migrationBuilder.DropTable(
                name: "ThreadAttachments");

            migrationBuilder.DropIndex(
                name: "IX_Threads_Status_BusinessDigestedAt",
                table: "Threads");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ProjectKey",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_LongMemories_ProjectKey_Type",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "BusinessDigestedAt",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryEnabled",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryIntervalHours",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryModel",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Database",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Host",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Options",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_PasswordProtected",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Port",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Provider",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BusinessMemoryStore_Username",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "DeveloperName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ManualKey",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectKey",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectKeySource",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectKey",
                table: "LongMemories");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                table: "LongMemories");
        }
    }
}
