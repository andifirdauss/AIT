using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubNodeTokens",
                table: "Threads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PlannerToolDelegations",
                columns: table => new
                {
                    PlannerNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tool = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerToolDelegations", x => new { x.PlannerNodeId, x.Tool });
                    table.ForeignKey(
                        name: "FK_PlannerToolDelegations_Nodes_PlannerNodeId",
                        column: x => x.PlannerNodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannerToolDelegations_Nodes_SubNodeId",
                        column: x => x.SubNodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerToolDelegations_SubNodeId",
                table: "PlannerToolDelegations",
                column: "SubNodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerToolDelegations");

            migrationBuilder.DropColumn(
                name: "SubNodeTokens",
                table: "Threads");
        }
    }
}
