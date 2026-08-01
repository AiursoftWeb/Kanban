using System.ComponentModel;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Notifications;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class ShareWriteTools(
    TemplateDbContext db,
    IAuthorizationService authorizationService,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Share a board with a user or role. IMPORTANT: You MUST provide EXACTLY ONE of targetUserId or targetRoleId — the other MUST be left empty (do NOT pass 'None', 'null', or any placeholder). Use SearchUsers to find user IDs.")]
    [Advice]
    public async Task<string> ShareBoard(
        [Description("Board ID to share")] int boardId,
        [Description("User ID to share with. Leave empty if sharing with a role instead.")] string? targetUserId,
        [Description("Role ID to share with. Leave empty if sharing with a user instead.")] string? targetRoleId,
        [Description("Permission level: ReadOnly or Editable")] string permission)
    {
        var userId = currentUser.UserId;

        // Normalize sentinel values that LLMs may pass instead of leaving the parameter empty.
        targetUserId = NormalizeOptionalId(targetUserId);
        targetRoleId = NormalizeOptionalId(targetRoleId);

        if (targetUserId == null && targetRoleId == null)
            return "Error: You must specify exactly one of targetUserId or targetRoleId. Leave the other empty — do not pass 'None' or 'null'.";

        if (targetUserId != null && targetRoleId != null)
            return "Error: You must specify exactly one of targetUserId or targetRoleId, not both. Leave the other empty.";

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return $"Error: Board #{boardId} not found.";
        if (!await CanManageSharesAsync(board, userId)) return "Error: Only the board owner can manage shares.";

        if (!Enum.TryParse<SharePermission>(permission, true, out var sharePermission))
            return $"Error: Invalid permission \"{permission}\". Valid values: ReadOnly, Editable.";

        if (targetUserId != null)
        {
            var userExists = await db.Users.AnyAsync(u => u.Id == targetUserId);
            if (!userExists) return $"Error: User \"{targetUserId}\" not found. Use SearchUsers to find valid user IDs.";
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

    /// <summary>
    /// Normalizes optional ID parameters so that common LLM placeholder values
    /// (such as "None", "null", "string", or whitespace) are treated as null.
    /// </summary>
    private static string? NormalizeOptionalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("string", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("undefined", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    [McpServerTool, Description("Remove a share from a board. Use GetBoardShares to see share IDs.")]
    [Advice]
    public async Task<string> RemoveBoardShare(
        [Description("Share ID to remove")] Guid shareId)
    {
        var userId = currentUser.UserId;
        var share = await db.BoardShares
            .Include(s => s.Board)
            .FirstOrDefaultAsync(s => s.Id == shareId);

        if (share == null) return "Error: Share not found.";
        if (!await CanManageSharesAsync(share.Board, userId)) return "Error: Only the board owner can manage shares.";

        db.BoardShares.Remove(share);
        await db.SaveChangesAsync();
        await CardSubscriptionService.RemoveSubscriptionsWithoutBoardAccessAsync(db, share.BoardId);
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
        if (!await CanManageSharesAsync(board, userId)) return "Error: Only the board owner can change visibility.";

        board.IsPublic = isPublic;
        await db.SaveChangesAsync();
        if (!isPublic)
        {
            await CardSubscriptionService.RemoveSubscriptionsWithoutBoardAccessAsync(db, board.Id);
            await db.SaveChangesAsync();
        }

        var visibility = isPublic ? "public" : "private";
        return $"Board \"{board.Name}\" is now {visibility}.";
    }

    private async Task<bool> CanManageSharesAsync(KanbanBoard board, string userId)
    {
        if (board.UserId == userId) return true;
        var authResult = await authorizationService.AuthorizeAsync(
            new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier, userId)
                ])),
            AppPermissionNames.CanManageAnyBoardShare);
        return authResult.Succeeded;
    }
}
