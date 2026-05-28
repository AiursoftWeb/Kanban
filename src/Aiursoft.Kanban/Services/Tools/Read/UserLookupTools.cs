using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class UserLookupTools(
    TemplateDbContext db,
    KanbanAccessService access) : IScopedDependency
{
    [McpServerTool, Description("Get members who have access to a board")]
    public async Task<string> GetBoardMembers(
        [Description("Board ID")] int boardId,
        [Description("Current user ID")] string userId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var accessibleUserIds = await access.GetAccessibleBoardUserIdsAsync(board);
        var users = await db.Users
            .Where(u => accessibleUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        if (users.Count == 0) return "No members found for this board.";

        var lines = new List<string> { $"Members of board \"{board.Name}\" ({users.Count} total):" };
        foreach (var user in users)
        {
            var displayName = KanbanAccessService.GetUserDisplayName(user);
            var isOwner = user.Id == board.UserId ? " (Owner)" : "";
            lines.Add($"- {displayName} (ID: {user.Id}){isOwner}");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Search for users by display name or username")]
    public async Task<string> SearchUsers(
        [Description("Search query")] string query)
    {
        var normalized = query.Trim().ToUpperInvariant();
        var users = await db.Users
            .Where(u => u.DisplayName.ToUpper().Contains(normalized) ||
                        (u.UserName != null && u.UserName.ToUpper().Contains(normalized)) ||
                        (u.Email != null && u.Email.ToUpper().Contains(normalized)))
            .Take(10)
            .ToListAsync();

        if (users.Count == 0) return $"No users found matching \"{query}\".";

        return string.Join("\n", users.Select(u =>
        {
            var name = KanbanAccessService.GetUserDisplayName(u);
            return $"- {name} (ID: {u.Id})";
        }));
    }
}
