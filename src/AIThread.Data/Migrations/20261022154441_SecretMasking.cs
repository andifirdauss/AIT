using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIThread.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecretMasking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PassthroughSecretMasking",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrivacyNoticeAcknowledgedAt",
                table: "SystemSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecretMaskingEnabled",
                table: "SystemSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PassthroughSecretMasking",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PrivacyNoticeAcknowledgedAt",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SecretMaskingEnabled",
                table: "SystemSettings");
        }
    }
}
