using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class CodeFunctionCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeFunctionScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StartLine = table.Column<int>(type: "int", nullable: false),
                    EndLine = table.Column<int>(type: "int", nullable: false),
                    BodyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Tokens = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeFunctionScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeFunctionScans_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeFunctionCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeFunctionScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Line = table.Column<int>(type: "int", nullable: false),
                    ResolvedPath = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    ResolvedSymbol = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResolvedLine = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeFunctionCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeFunctionCalls_CodeFunctionScans_CodeFunctionScanId",
                        column: x => x.CodeFunctionScanId,
                        principalTable: "CodeFunctionScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeFunctionCalls_CodeFunctionScanId",
                table: "CodeFunctionCalls",
                column: "CodeFunctionScanId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeFunctionScans_ProjectId_Key",
                table: "CodeFunctionScans",
                columns: new[] { "ProjectId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeFunctionScans_ProjectId_Path",
                table: "CodeFunctionScans",
                columns: new[] { "ProjectId", "Path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeFunctionCalls");

            migrationBuilder.DropTable(
                name: "CodeFunctionScans");
        }
    }
}
