using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddCardTimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColumnStatus",
                table: "KanbanColumns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualEndTime",
                table: "KanbanCards",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualStartTime",
                table: "KanbanCards",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "KanbanCards",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedStartTime",
                table: "KanbanCards",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColumnStatus",
                table: "KanbanColumns");

            migrationBuilder.DropColumn(
                name: "ActualEndTime",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "ActualStartTime",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "PlannedStartTime",
                table: "KanbanCards");
        }
    }
}
