using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RestrictKanbanBoardDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards");

            migrationBuilder.AddForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards");

            migrationBuilder.AddForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
