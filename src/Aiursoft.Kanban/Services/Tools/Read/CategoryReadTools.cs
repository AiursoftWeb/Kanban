using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class CategoryReadTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Get category IDs, names, default flags, and order for a board. Categories are independent of status columns and labels; every card has one category.")]
    public async Task<string> GetCategories([Description("Board ID")] int boardId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null || !await access.HasReadAccess(board, currentUser.UserId))
            return "Error: permission_denied: You do not have permission to view this board.";

        var categories = await db.KanbanCategories.Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Order).ThenBy(c => c.Id).ToListAsync();
        if (categories.Count == 0) return "No categories found.";
        return string.Join("\n", categories.Select(c => $"- #{c.Id} \"{c.Name}\" (IsDefault: {c.IsDefault}, Order: {c.Order})"));
    }
}
