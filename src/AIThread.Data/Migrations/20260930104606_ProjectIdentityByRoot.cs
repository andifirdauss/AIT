using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectIdentityByRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Projects used to be unique per .sln file; now they are unique per root folder. Two solutions in the same
            // folder therefore become one project: keep the most recently used one and move the threads, memories,
            // log lines and Telegram selections of the others onto it before the unique index on RootPath is built.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT Id, RootPath,
                           ROW_NUMBER() OVER (PARTITION BY RootPath ORDER BY LastSeenAt DESC, CreatedAt DESC, Id) AS rn
                    FROM Projects)
                SELECT d.Id AS DuplicateId, s.Id AS SurvivorId
                INTO #ProjectMerge
                FROM ranked d
                JOIN ranked s ON s.RootPath = d.RootPath AND s.rn = 1
                WHERE d.rn > 1;

                UPDATE t SET t.ProjectId = m.SurvivorId FROM Threads t JOIN #ProjectMerge m ON t.ProjectId = m.DuplicateId;
                UPDATE t SET t.ProjectId = m.SurvivorId FROM LongMemories t JOIN #ProjectMerge m ON t.ProjectId = m.DuplicateId;
                UPDATE t SET t.ProjectId = m.SurvivorId FROM ActivityLogs t JOIN #ProjectMerge m ON t.ProjectId = m.DuplicateId;
                UPDATE t SET t.ActiveProjectId = m.SurvivorId FROM TelegramChats t JOIN #ProjectMerge m ON t.ActiveProjectId = m.DuplicateId;
                DELETE p FROM Projects p JOIN #ProjectMerge m ON p.Id = m.DuplicateId;

                DROP TABLE #ProjectMerge;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Projects_SolutionPath",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SolutionPath",
                table: "Projects");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_RootPath",
                table: "Projects",
                column: "RootPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_RootPath",
                table: "Projects");

            migrationBuilder.AddColumn<string>(
                name: "SolutionPath",
                table: "Projects",
                type: "nvarchar(800)",
                maxLength: 800,
                nullable: false,
                defaultValue: "");

            // The .sln path cannot be recovered; the root folder keeps the column unique. Earlier versions report the
            // project as missing until its solution is registered again.
            migrationBuilder.Sql("UPDATE Projects SET SolutionPath = RootPath");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_SolutionPath",
                table: "Projects",
                column: "SolutionPath",
                unique: true);
        }
    }
}
