using System.Security.Claims;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.KanbanViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[LimitPerMin]
[Route("PublicKanban/View/{boardId:int}")]
public class PublicKanbanController(TemplateDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> View(int boardId)
    {
        var board = await db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(cd => cd.Order))
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await HasReadAccess(board, userId))
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        var canEdit = userId != null && await HasEditAccess(board, userId);

        return this.StackView(new PublicBoardViewModel(board.Name)
        {
            Board = board,
            CanEdit = canEdit
        });
    }

    private async Task<bool> HasReadAccess(KanbanBoard board, string? userId)
    {
        if (board.IsPublic) return true;
        if (userId == null) return false;
        if (board.UserId == userId) return true;
        var userRoles = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        return await db.BoardShares.AnyAsync(s => s.BoardId == board.Id &&
            (s.SharedWithUserId == userId || (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    private async Task<bool> HasEditAccess(KanbanBoard board, string userId)
    {
        if (board.UserId == userId) return true;
        var userRoles = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        return await db.BoardShares.AnyAsync(s => s.BoardId == board.Id && s.Permission == SharePermission.Editable &&
            (s.SharedWithUserId == userId || (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }
}
