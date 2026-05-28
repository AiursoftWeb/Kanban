using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class BoardReadTools(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanAccessService access) : IScopedDependency
{
    [McpServerTool, Description("Get all kanban boards owned by the current user")]
    public async Task<string> GetUserBoards(
        [Description("Current user ID")] string userId)
    {
        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Include(b => b.Columns)
            .OrderBy(b => b.Order)
            .ToListAsync();

        if (boards.Count == 0)
            return "You don't have any boards yet.";

        var lines = new List<string> { $"Found {boards.Count} board(s):" };
        foreach (var board in boards)
        {
            var columnNames = board.Columns.OrderBy(c => c.Order).Select(c => c.Name);
            lines.Add($"- Board #{board.Id} \"{board.Name}\" (Columns: {string.Join(", ", columnNames)})");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get detailed information about a specific board by its ID")]
    public async Task<string> GetBoardById(
        [Description("Board ID")] int boardId,
        [Description("Current user ID")] string userId)
    {
        var board = await db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(card => card.Order))
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var lines = new List<string> { $"Board #{board.Id}: \"{board.Name}\"" };
        foreach (var col in board.Columns)
        {
            lines.Add($"  Column \"{col.Name}\" (#{col.Id}, Status: {col.ColumnStatus}):");
            foreach (var card in col.Cards)
            {
                var assignee = card.AssignedUserId != null
                    ? (await userManager.FindByIdAsync(card.AssignedUserId))?.DisplayName ?? card.AssignedUserId
                    : "unassigned";
                lines.Add($"    - #{card.Id} [{card.Priority}] {card.Title} (assigned: {assignee})");
            }
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Search boards by name")]
    public async Task<string> SearchBoards(
        [Description("Search query")] string query,
        [Description("Current user ID")] string userId)
    {
        var normalized = query.Trim().ToUpperInvariant();
        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId && b.Name.ToUpper().Contains(normalized))
            .OrderBy(b => b.Order)
            .ToListAsync();

        if (boards.Count == 0) return $"No boards found matching \"{query}\".";
        return string.Join("\n", boards.Select(b => $"- Board #{b.Id} \"{b.Name}\""));
    }
}
