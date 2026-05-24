using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Kanban.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "KanbanCards",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.CreateTable(
                name: "KanbanLabels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KanbanLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KanbanCardLabels",
                columns: table => new
                {
                    CardId = table.Column<int>(type: "INTEGER", nullable: false),
                    LabelId = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

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
