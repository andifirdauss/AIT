using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeFallbackAndFileSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "Threads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RolledBackAt",
                table: "Threads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FallbackNodeId",
                table: "Nodes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ThreadFileSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThreadRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Existed = table.Column<bool>(type: "bit", nullable: false),
                    OriginalContent = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    OriginalHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AfterHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreadFileSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreadFileSnapshots_Threads_ThreadRunId",
                        column: x => x.ThreadRunId,
                        principalTable: "Threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_FallbackNodeId",
                table: "Nodes",
                column: "FallbackNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ThreadFileSnapshots_ThreadRunId_FilePath",
                table: "ThreadFileSnapshots",
                columns: new[] { "ThreadRunId", "FilePath" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Nodes_Nodes_FallbackNodeId",
                table: "Nodes",
                column: "FallbackNodeId",
                principalTable: "Nodes",
                principalColumn: "Id");

            // Active planners used to fall back to the next active planner automatically; keep that behaviour as an
            // explicit, editable chain in priority order.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT Id, LEAD(Id) OVER (ORDER BY Priority, Name) AS NextId
                    FROM Nodes
                    WHERE Role = 'Planner' AND IsActive = 1
                )
                UPDATE n SET FallbackNodeId = o.NextId
                FROM Nodes n
                JOIN ordered o ON n.Id = o.Id
                WHERE o.NextId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Nodes_Nodes_FallbackNodeId",
                table: "Nodes");

            migrationBuilder.DropTable(
                name: "ThreadFileSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Nodes_FallbackNodeId",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "RolledBackAt",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "FallbackNodeId",
                table: "Nodes");
        }
    }
}
