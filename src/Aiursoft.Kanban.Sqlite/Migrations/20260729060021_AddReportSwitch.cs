using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddReportSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableDailyReport",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableWeeklyReport",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableDailyReport",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EnableWeeklyReport",
                table: "AspNetUsers");
        }
    }
}
