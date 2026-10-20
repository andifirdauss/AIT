using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSubNodeToToolsNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nodes created with the earlier name keep their role.
            migrationBuilder.Sql("UPDATE Nodes SET Role = N'ToolsNode' WHERE Role = N'SubNode'");

            migrationBuilder.DropForeignKey(
                name: "FK_PlannerToolDelegations_Nodes_SubNodeId",
                table: "PlannerToolDelegations");

            migrationBuilder.RenameColumn(
                name: "SubNodeTokens",
                table: "Threads",
                newName: "ToolsNodeTokens");

            migrationBuilder.RenameColumn(
                name: "SubNodeId",
                table: "PlannerToolDelegations",
                newName: "ToolsNodeId");

            migrationBuilder.RenameIndex(
                name: "IX_PlannerToolDelegations_SubNodeId",
                table: "PlannerToolDelegations",
                newName: "IX_PlannerToolDelegations_ToolsNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlannerToolDelegations_Nodes_ToolsNodeId",
                table: "PlannerToolDelegations",
                column: "ToolsNodeId",
                principalTable: "Nodes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Nodes SET Role = N'SubNode' WHERE Role = N'ToolsNode'");

            migrationBuilder.DropForeignKey(
                name: "FK_PlannerToolDelegations_Nodes_ToolsNodeId",
                table: "PlannerToolDelegations");

            migrationBuilder.RenameColumn(
                name: "ToolsNodeTokens",
                table: "Threads",
                newName: "SubNodeTokens");

            migrationBuilder.RenameColumn(
                name: "ToolsNodeId",
                table: "PlannerToolDelegations",
                newName: "SubNodeId");

            migrationBuilder.RenameIndex(
                name: "IX_PlannerToolDelegations_ToolsNodeId",
                table: "PlannerToolDelegations",
                newName: "IX_PlannerToolDelegations_SubNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlannerToolDelegations_Nodes_SubNodeId",
                table: "PlannerToolDelegations",
                column: "SubNodeId",
                principalTable: "Nodes",
                principalColumn: "Id");
        }
    }
}
