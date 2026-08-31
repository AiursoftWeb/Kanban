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
}
