using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "KanbanBoards",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "KanbanBoards",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BoardShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BoardId = table.Column<int>(type: "int", nullable: false),
                    SharedWithUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SharedWithRoleId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Permission = table.Column<int>(type: "int", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardShares_AspNetUsers_SharedWithUserId",
                        column: x => x.SharedWithUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BoardShares_KanbanBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "KanbanBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanBoards_UserId",
                table: "KanbanBoards",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardShares_BoardId",
                table: "BoardShares",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardShares_SharedWithUserId",
                table: "BoardShares",
                column: "SharedWithUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KanbanBoards_AspNetUsers_UserId",
                table: "KanbanBoards");

            migrationBuilder.DropTable(
                name: "BoardShares");

            migrationBuilder.DropIndex(
                name: "IX_KanbanBoards_UserId",
                table: "KanbanBoards");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "KanbanBoards");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "KanbanBoards");
        }
    }
}
