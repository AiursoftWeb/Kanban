using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class UserLookupTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Get members who have access to a board")]
    public async Task<string> GetBoardMembers(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
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

    [McpServerTool, Description("Get all shares (user and role) for a board, including share IDs needed for removal")]
    public async Task<string> GetBoardShares(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (board.UserId != userId) return "Error: Only the board owner can view share details.";

        var shares = await db.BoardShares
            .Include(s => s.SharedWithUser)
            .Where(s => s.BoardId == boardId)
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync();

        if (shares.Count == 0) return $"Board \"{board.Name}\" has no shares.";

        var lines = new List<string> { $"Shares for board \"{board.Name}\" ({shares.Count} total):" };
        foreach (var share in shares)
        {
            var target = share.SharedWithUserId != null
                ? $"User: {KanbanAccessService.GetUserDisplayName(share.SharedWithUser)} ({share.SharedWithUserId})"
                : $"Role: {share.SharedWithRoleId}";
            lines.Add($"- Share #{share.Id}: {target}, Permission: {share.Permission}");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Search for users globally by display name, username, or email. Use this to find users to share boards with.")]
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
