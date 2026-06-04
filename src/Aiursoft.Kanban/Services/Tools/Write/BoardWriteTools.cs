using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class BoardWriteTools(
    TemplateDbContext db,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Create a new kanban board with default columns (To Do, In Progress, Done)")]
    [Advice]
    public async Task<string> CreateBoard(
        [Description("Board name")] string name)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Board name is required.";

        var maxOrder = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .MaxAsync(b => (int?)b.Order) ?? 0;

        var board = new KanbanBoard { Name = name.Trim(), UserId = userId, Order = maxOrder + 100 };
        db.KanbanBoards.Add(board);

        var defaultColumns = new[]
        {
            new KanbanColumn { Name = "To Do", Order = 0, Board = board, ColumnStatus = ColumnStatus.NotStarted },
            new KanbanColumn { Name = "In Progress", Order = 1, Board = board, ColumnStatus = ColumnStatus.InProgress },
            new KanbanColumn { Name = "Done", Order = 2, Board = board, ColumnStatus = ColumnStatus.Completed }
        };
        db.KanbanColumns.AddRange(defaultColumns);
        await db.SaveChangesAsync();

        return $"Board created: #{board.Id} \"{board.Name}\" with columns: To Do, In Progress, Done.";
    }

    [McpServerTool, Description("Rename an existing board")]
    [Advice]
    public async Task<string> RenameBoard(
        [Description("Board ID")] int boardId,
        [Description("New board name")] string name)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Board name is required.";

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Error: Board not found.";
        if (board.UserId != userId) return "Error: Only the board owner can rename it.";

        var oldName = board.Name;
        board.Name = name.Trim();
        await db.SaveChangesAsync();

        return $"Board #{boardId} renamed from \"{oldName}\" to \"{board.Name}\".";
    }

    [McpServerTool, Description("Delete a board and all its columns, cards, and shares. This cannot be undone.")]
    [Advice]
    public async Task<string> DeleteBoard(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards
            .Include(b => b.Columns).ThenInclude(c => c.Cards).ThenInclude(c => c.CardLabels)
            .Include(b => b.BoardShares)
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return "Error: Board not found.";
        if (board.UserId != userId) return "Error: Only the board owner can delete it.";

        foreach (var column in board.Columns.ToList())
        {
            db.KanbanCards.RemoveRange(column.Cards);
            db.KanbanColumns.Remove(column);
        }

        db.BoardShares.RemoveRange(board.BoardShares);
        db.KanbanBoards.Remove(board);
        await db.SaveChangesAsync();

        return $"Board #{boardId} \"{board.Name}\" and all its contents have been deleted.";
    }
}
