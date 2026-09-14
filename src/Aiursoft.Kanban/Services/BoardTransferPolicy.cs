using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services;

public static class BoardTransferPolicy
{
    public static async Task<bool> BelongToSameUserGroupAsync(
        this TemplateDbContext db,
        int sourceBoardId,
        int targetBoardId)
    {
        var sourceGroupIds = await db.BoardShares
            .Where(share => share.BoardId == sourceBoardId && share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .ToListAsync();
        return sourceGroupIds.Count != 0 && await db.BoardShares.AnyAsync(share =>
            share.BoardId == targetBoardId &&
            share.SharedWithRoleId != null &&
            sourceGroupIds.Contains(share.SharedWithRoleId));
    }
}
