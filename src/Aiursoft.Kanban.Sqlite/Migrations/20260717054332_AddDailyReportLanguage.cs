using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyReportLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DailyReportLanguage",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyReportLanguage",
                table: "AspNetUsers");
        }
    }
}
