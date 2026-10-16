using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiaiManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTatamiFreeFightMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFreeFightModeEnabled",
                table: "Tatamis",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFreeFightModeEnabled",
                table: "Tatamis");
        }
    }
}
