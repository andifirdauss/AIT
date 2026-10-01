using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewerModel",
                table: "Threads",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewerTokens",
                table: "Threads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VerificationJson",
                table: "Threads",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VerificationMs",
                table: "Threads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BuildEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // The existing settings row gets the same defaults as a new one (SystemSetting).
            migrationBuilder.AddColumn<int>(
                name: "BuildTimeoutSeconds",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 300);

            migrationBuilder.AddColumn<int>(
                name: "MaxFixRounds",
                table: "SystemSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewerModel",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "ReviewerTokens",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "VerificationJson",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "VerificationMs",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "BuildEnabled",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "BuildTimeoutSeconds",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MaxFixRounds",
                table: "SystemSettings");
        }
    }
}
