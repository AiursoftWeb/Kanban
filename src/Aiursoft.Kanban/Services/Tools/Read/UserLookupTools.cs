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

    [McpServerTool, Description("Search for users by display name or username, scoped to users who share boards with you")]
    public async Task<string> SearchUsers(
        [Description("Search query")] string query)
    {
        var userId = currentUser.UserId;

        // Find all users who share at least one board with the current user
        var ownBoardIds = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Select(b => b.Id)
            .ToListAsync();

        var sharedBoardIds = await db.BoardShares
            .Where(s => s.SharedWithUserId == userId)
            .Select(s => s.BoardId)
            .ToListAsync();

        var allBoardIds = ownBoardIds.Union(sharedBoardIds).ToList();

        // Collect user IDs from boards the caller has access to
        var accessibleUserIds = new HashSet<string> { userId };

        var ownerIds = await db.KanbanBoards
            .Where(b => allBoardIds.Contains(b.Id))
            .Select(b => b.UserId)
            .ToListAsync();
        foreach (var id in ownerIds) if (id != null) accessibleUserIds.Add(id);

        var shareIds = await db.BoardShares
            .Where(s => allBoardIds.Contains(s.BoardId) && s.SharedWithUserId != null)
            .Select(s => s.SharedWithUserId!)
            .ToListAsync();
        foreach (var id in shareIds) accessibleUserIds.Add(id);

        var normalized = query.Trim().ToUpperInvariant();
        var users = await db.Users
            .Where(u => accessibleUserIds.Contains(u.Id) &&
                        (u.DisplayName.ToUpper().Contains(normalized) ||
                         (u.UserName != null && u.UserName.ToUpper().Contains(normalized)) ||
                         (u.Email != null && u.Email.ToUpper().Contains(normalized))))
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
