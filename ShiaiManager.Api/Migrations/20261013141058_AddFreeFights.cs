using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiaiManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeFights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Fights",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "BlueName",
                table: "Fights",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoldenScoreDurationSeconds",
                table: "Fights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GoldenScoreEnabled",
                table: "Fights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Fights",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<int>(
                name: "MatchDurationSeconds",
                table: "Fights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhiteName",
                table: "Fights",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinnerSide",
                table: "Fights",
                type: "TEXT",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlueName",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "GoldenScoreDurationSeconds",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "GoldenScoreEnabled",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "MatchDurationSeconds",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "WhiteName",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "WinnerSide",
                table: "Fights");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "Fights",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
