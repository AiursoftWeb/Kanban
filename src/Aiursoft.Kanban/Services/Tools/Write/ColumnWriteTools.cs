using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class ColumnWriteTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Create a new column on a board")]
    [Advice]
    public async Task<string> CreateColumn(
        [Description("Board ID")] int boardId,
        [Description("Column name")] string name)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Column name is required.";

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Error: Board not found.";
        if (!await access.HasEditAccess(board, userId)) return "Error: You do not have permission to edit this board.";

        var maxOrder = await db.KanbanColumns
            .Where(c => c.BoardId == boardId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        var column = new KanbanColumn { Name = name.Trim(), Order = maxOrder + 1, BoardId = boardId };
        db.KanbanColumns.Add(column);
        await db.SaveChangesAsync();

        return $"Column created: #{column.Id} \"{column.Name}\" on board \"{board.Name}\".";
    }

    [McpServerTool, Description("Rename a column")]
    [Advice]
    public async Task<string> RenameColumn(
        [Description("Column ID")] int columnId,
        [Description("New column name")] string name)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(name))
            return "Error: Column name is required.";

        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId)) return "Error: You do not have permission to edit this board.";

        var oldName = column.Name;
        column.Name = name.Trim();
        await db.SaveChangesAsync();

        return $"Column #{columnId} renamed from \"{oldName}\" to \"{column.Name}\".";
    }

    [McpServerTool, Description("Delete a column and all its cards")]
    [Advice]
    public async Task<string> DeleteColumn(
        [Description("Column ID")] int columnId)
    {
        var userId = currentUser.UserId;
        var column = await db.KanbanColumns
            .Include(c => c.Cards)
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId)) return "Error: You do not have permission to edit this board.";

        var cardCount = column.Cards.Count;
        db.KanbanCards.RemoveRange(column.Cards);
        db.KanbanColumns.Remove(column);
        await db.SaveChangesAsync();

        return $"Column \"{column.Name}\" and its {cardCount} card(s) have been deleted.";
    }

    [McpServerTool, Description("Update the status of a column (NotStarted=0, InProgress=1, Completed=2)")]
    [Advice]
    public async Task<string> UpdateColumnStatus(
        [Description("Column ID")] int columnId,
        [Description("New status: 0=NotStarted, 1=InProgress, 2=Completed")] int status)
    {
        var userId = currentUser.UserId;
        if (!Enum.IsDefined(typeof(ColumnStatus), status))
            return "Error: Invalid column status. Use 0 (NotStarted), 1 (InProgress), or 2 (Completed).";

        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId)) return "Error: You do not have permission to edit this board.";

        column.ColumnStatus = (ColumnStatus)status;
        await db.SaveChangesAsync();

        return $"Column \"{column.Name}\" status updated to {(ColumnStatus)status}.";
    }

    [McpServerTool, Description("Move a column to a new position (index) on its board")]
    [Advice]
    public async Task<string> MoveColumn(
        [Description("Column ID")] int columnId,
        [Description("New position index (0-based)")] int newOrder)
    {
        var userId = currentUser.UserId;
        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId)) return "Error: You do not have permission to edit this board.";

        var columns = await db.KanbanColumns
            .Where(c => c.BoardId == column.BoardId && c.Id != columnId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var allColumns = new List<KanbanColumn>();
        var idx = 0;
        foreach (var existing in columns)
        {
            if (idx == newOrder) allColumns.Add(column);
            allColumns.Add(existing);
            idx++;
        }
        if (idx <= newOrder) allColumns.Add(column);

        for (var i = 0; i < allColumns.Count; i++)
            allColumns[i].Order = i;

        await db.SaveChangesAsync();
        return $"Column \"{column.Name}\" moved to position {newOrder}.";
    }
}
