using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.Access;

public class KanbanAccessService : IScopedDependency
{
    private readonly TemplateDbContext _db;

    public KanbanAccessService(TemplateDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasReadAccess(KanbanBoard board, string userId)
    {
        if (board.IsPublic) return true;
        if (board.UserId == userId) return true;
        var userRoles = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        return await _db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    public async Task<bool> HasEditAccess(KanbanBoard board, string userId)
    {
        if (board.UserId == userId) return true;
        var userRoles = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        return await _db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            s.Permission == SharePermission.Editable &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    public async Task<HashSet<string>> GetAccessibleBoardUserIdsAsync(KanbanBoard board)
    {
        var accessibleUserIds = await _db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithUserId != null)
            .Select(share => share.SharedWithUserId!)
            .ToHashSetAsync();

        var roleIds = await _db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .ToListAsync();

        var roleUserIds = await _db.UserRoles
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId)
            .ToListAsync();
        accessibleUserIds.UnionWith(roleUserIds);

        if (!string.IsNullOrWhiteSpace(board.UserId))
            accessibleUserIds.Add(board.UserId);

        return accessibleUserIds;
    }

    public async Task<bool> CanAssignUserToBoardAsync(KanbanBoard board, string? assignedUserId)
    {
        if (assignedUserId == null) return true;
        if (!await _db.Users.AnyAsync(user => user.Id == assignedUserId)) return false;
        return (await GetAccessibleBoardUserIdsAsync(board)).Contains(assignedUserId);
    }

    public static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }

    public static string GetUserInitial(User? user)
    {
        var displayName = GetUserDisplayName(user);
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim()[0].ToString().ToUpperInvariant();
    }
}
