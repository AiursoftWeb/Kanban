using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class ShareWriteTools(
    TemplateDbContext db,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Share a board with a user or role. Use SearchUsers to find user IDs.")]
    [Advice]
    public async Task<string> ShareBoard(
        [Description("Board ID to share")] int boardId,
        [Description("User ID to share with, or null if sharing with a role")] string? targetUserId,
        [Description("Role ID to share with, or null if sharing with a user")] string? targetRoleId,
        [Description("Permission level: ReadOnly or Editable")] string permission)
    {
        var userId = currentUser.UserId;
        if (targetUserId == null && targetRoleId == null)
            return "Error: You must specify either a user ID or a role ID to share with.";

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Error: Board not found.";
        if (board.UserId != userId) return "Error: Only the board owner can manage shares.";

        if (!Enum.TryParse<SharePermission>(permission, true, out var sharePermission))
            return $"Error: Invalid permission \"{permission}\". Valid values: ReadOnly, Editable.";

        if (targetUserId != null)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == targetUserId);
            if (!userExists) return $"Error: User \"{targetUserId}\" not found.";
            if (targetUserId == userId) return "Error: You cannot share the board with yourself.";
        }

        if (targetRoleId != null)
        {
            var roleExists = await db.Roles.AnyAsync(r => r.Id == targetRoleId);
            if (!roleExists) return $"Error: Role \"{targetRoleId}\" not found.";
        }

        var exists = await db.BoardShares.AnyAsync(s =>
            s.BoardId == boardId &&
            ((targetUserId != null && s.SharedWithUserId == targetUserId) ||
             (targetRoleId != null && s.SharedWithRoleId == targetRoleId)));

        if (exists) return "Error: This user or role already has access to the board.";

        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            SharedWithUserId = targetUserId,
            SharedWithRoleId = targetRoleId,
            Permission = sharePermission
        });
        await db.SaveChangesAsync();

        var target = targetUserId != null
            ? $"user {targetUserId}"
            : $"role {targetRoleId}";
        return $"Board \"{board.Name}\" shared with {target} with {sharePermission} permission.";
    }

    [McpServerTool, Description("Remove a share from a board. Use GetBoardMembers to see existing shares (share IDs are available in the board details).")]
    [Advice]
    public async Task<string> RemoveBoardShare(
        [Description("Share ID to remove")] Guid shareId)
    {
        var userId = currentUser.UserId;
        var share = await db.BoardShares
            .Include(s => s.Board)
            .FirstOrDefaultAsync(s => s.Id == shareId);

        if (share == null) return "Error: Share not found.";
        if (share.Board.UserId != userId) return "Error: Only the board owner can manage shares.";

        db.BoardShares.Remove(share);
        await db.SaveChangesAsync();

        return $"Share removed from board \"{share.Board.Name}\".";
    }

    [McpServerTool, Description("Set a board's visibility. When public, anyone can view it. When private, only members can view.")]
    [Advice]
    public async Task<string> UpdateBoardVisibility(
        [Description("Board ID")] int boardId,
        [Description("Set to true for public, false for private")] bool isPublic)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Error: Board not found.";
        if (board.UserId != userId) return "Error: Only the board owner can change visibility.";

        board.IsPublic = isPublic;
        await db.SaveChangesAsync();

        var visibility = isPublic ? "public" : "private";
        return $"Board \"{board.Name}\" is now {visibility}.";
    }
}
