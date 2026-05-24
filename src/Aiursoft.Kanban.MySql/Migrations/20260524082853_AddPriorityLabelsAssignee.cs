using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityLabelsAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedUserId",
                table: "KanbanCards",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "KanbanCards",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.CreateTable(
                name: "KanbanLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Color = table.Column<string>(type: "varchar(7)", maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KanbanLabels", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KanbanCardLabels",
                columns: table => new
                {
                    CardId = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KanbanCardLabels", x => new { x.CardId, x.LabelId });
                    table.ForeignKey(
                        name: "FK_KanbanCardLabels_KanbanCards_CardId",
                        column: x => x.CardId,
                        principalTable: "KanbanCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KanbanCardLabels_KanbanLabels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "KanbanLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanCards_AssignedUserId",
                table: "KanbanCards",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanCardLabels_LabelId",
                table: "KanbanCardLabels",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_KanbanLabels_Name",
                table: "KanbanLabels",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_KanbanCards_AspNetUsers_AssignedUserId",
                table: "KanbanCards",
                column: "AssignedUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KanbanCards_AspNetUsers_AssignedUserId",
                table: "KanbanCards");

            migrationBuilder.DropTable(
                name: "KanbanCardLabels");

            migrationBuilder.DropTable(
                name: "KanbanLabels");

            migrationBuilder.DropIndex(
                name: "IX_KanbanCards_AssignedUserId",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "KanbanCards");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "KanbanCards");
        }
    }
}
