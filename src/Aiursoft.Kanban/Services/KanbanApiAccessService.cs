using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services;

public sealed class KanbanApiAccessService(TemplateDbContext db) : IScopedDependency
{
    public async Task<bool> CanReadAsync(KanbanBoard board, string userId)
    {
        if (board.IsPublic || board.UserId == userId)
        {
            return true;
        }

        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        return await db.BoardShares.AnyAsync(share =>
            share.BoardId == board.Id &&
            (share.SharedWithUserId == userId ||
             (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId))));
    }

    public async Task<bool> CanEditAsync(KanbanBoard board, string userId)
    {
        if (board.IsArchived)
        {
            return false;
        }
        if (board.UserId == userId)
        {
            return true;
        }

        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        return await db.BoardShares.AnyAsync(share =>
            share.BoardId == board.Id &&
            share.Permission == SharePermission.Editable &&
            (share.SharedWithUserId == userId ||
             (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId))));
    }

    public async Task<HashSet<string>> GetAccessibleUserIdsAsync(KanbanBoard board)
    {
        var userIds = await db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithUserId != null)
            .Select(share => share.SharedWithUserId!)
            .ToHashSetAsync();

        var roleIds = await db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .ToListAsync();
        var roleUserIds = await db.UserRoles
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId)
            .ToListAsync();
        userIds.UnionWith(roleUserIds);

        if (!string.IsNullOrWhiteSpace(board.UserId))
        {
            userIds.Add(board.UserId);
        }
        return userIds;
    }

    public async Task<bool> CanAssignAsync(KanbanBoard board, string? assignedUserId)
    {
        if (assignedUserId == null)
        {
            return true;
        }
        if (!await db.Users.AnyAsync(user => user.Id == assignedUserId))
        {
            return false;
        }
        return (await GetAccessibleUserIdsAsync(board)).Contains(assignedUserId);
    }
}
