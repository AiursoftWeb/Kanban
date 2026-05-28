using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class ColumnReadTools(
    TemplateDbContext db,
    KanbanAccessService access) : IScopedDependency
{
    [McpServerTool, Description("Get all columns for a board")]
    public async Task<string> GetColumns(
        [Description("Board ID")] int boardId,
        [Description("Current user ID")] string userId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var columns = await db.KanbanColumns
            .Where(c => c.BoardId == boardId)
            .Include(c => c.Cards)
            .OrderBy(c => c.Order)
            .ToListAsync();

        if (columns.Count == 0) return "This board has no columns.";

        var lines = new List<string> { $"Columns for board \"{board.Name}\":" };
        foreach (var col in columns)
        {
            lines.Add($"- Column #{col.Id} \"{col.Name}\" (Status: {col.ColumnStatus}, Cards: {col.Cards.Count})");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get a single column by ID")]
    public async Task<string> GetColumnById(
        [Description("Column ID")] int columnId,
        [Description("Current user ID")] string userId)
    {
        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .Include(c => c.Cards)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null) return "Column not found.";
        if (!await access.HasReadAccess(column.Board, userId)) return "You do not have access to this board.";

        return $"Column #{column.Id} \"{column.Name}\" on board \"{column.Board.Name}\" (Status: {column.ColumnStatus}, {column.Cards.Count} cards)";
    }

    [McpServerTool, Description("Get all cards in a specific column")]
    public async Task<string> GetCardsInColumn(
        [Description("Column ID")] int columnId,
        [Description("Current user ID")] string userId)
    {
        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .Include(c => c.Cards.OrderBy(card => card.Order))
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null) return "Column not found.";
        if (!await access.HasReadAccess(column.Board, userId)) return "You do not have access to this board.";

        if (column.Cards.Count == 0) return $"Column \"{column.Name}\" has no cards.";

        var lines = new List<string> { $"Cards in column \"{column.Name}\":" };
        foreach (var card in column.Cards)
        {
            lines.Add($"- #{card.Id} [{card.Priority}] {card.Title}");
        }
        return string.Join("\n", lines);
    }
}
