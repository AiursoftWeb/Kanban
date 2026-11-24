using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.MySql.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class AddCardCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "KanbanCards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CategoryOrder",
                table: "KanbanCards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "KanbanCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    BoardId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KanbanCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KanbanCategories_KanbanBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "KanbanBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                INSERT INTO `KanbanCategories` (`Name`, `Order`, `IsDefault`, `BoardId`)
                SELECT 'Default', 0, 1, `Id` FROM `KanbanBoards`;
                """);
            migrationBuilder.Sql("""
                UPDATE `KanbanCards`
                SET `CategoryId` = (
                    SELECT category.`Id` FROM `KanbanCategories` category
                    INNER JOIN `KanbanColumns` status ON status.`BoardId` = category.`BoardId`
                    WHERE status.`Id` = `KanbanCards`.`ColumnId` AND category.`IsDefault` = 1
                ), `CategoryOrder` = `Order`;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_KanbanCards_CategoryId",
                table: "KanbanCards",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanCategories_BoardId_Name",
                table: "KanbanCategories",
                columns: new[] { "BoardId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_KanbanCards_KanbanCategories_CategoryId",
                table: "KanbanCards",
                column: "CategoryId",
                principalTable: "KanbanCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KanbanCards_KanbanCategories_CategoryId",
                table: "KanbanCards");

            migrationBuilder.DropIndex(
                name: "IX_KanbanCards_CategoryId",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "CategoryOrder",
                table: "KanbanCards");

            migrationBuilder.DropTable(
                name: "KanbanCategories");
        }
    }
}
