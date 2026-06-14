using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications;

internal static class NotificationRecipientFilter
{
    public static async Task<HashSet<string>> KeepUsersWithBoardReadAccess(
        TemplateDbContext db,
        int boardId,
        IEnumerable<string> userIds,
        CancellationToken ct)
    {
        var candidates = userIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .ToHashSet();
        if (candidates.Count == 0) return [];

        var board = await db.KanbanBoards
            .Where(b => b.Id == boardId)
            .Select(b => new { b.UserId, b.IsPublic })
            .FirstOrDefaultAsync(ct);
        if (board == null) return [];
        if (board.IsPublic) return candidates;

        var allowedUserIds = new HashSet<string>();
        if (!string.IsNullOrWhiteSpace(board.UserId) && candidates.Contains(board.UserId))
            allowedUserIds.Add(board.UserId);

        var directUserIds = await db.BoardShares
            .Where(share => share.BoardId == boardId &&
                            share.SharedWithUserId != null &&
                            candidates.Contains(share.SharedWithUserId))
            .Select(share => share.SharedWithUserId!)
            .ToListAsync(ct);
        allowedUserIds.UnionWith(directUserIds);

        var roleIds = await db.BoardShares
            .Where(share => share.BoardId == boardId && share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .ToListAsync(ct);
        if (roleIds.Count == 0) return allowedUserIds;

        var roleUserIds = await db.UserRoles
            .Where(userRole => candidates.Contains(userRole.UserId) && roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId)
            .ToListAsync(ct);
        allowedUserIds.UnionWith(roleUserIds);

        return allowedUserIds;
    }
}
