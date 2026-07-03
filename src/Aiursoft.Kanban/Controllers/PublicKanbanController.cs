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
    private static readonly string[] DotColors =
    [
        "dot-blue", "dot-orange", "dot-green", "dot-purple",
        "dot-pink", "dot-teal", "dot-amber", "dot-indigo"
    ];

    [HttpGet]
    public async Task<IActionResult> View(int boardId)
    {
        var board = await db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(cd => cd.Order))
                    .ThenInclude(card => card.CardLabels)
                        .ThenInclude(link => link.Label)
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(cd => cd.Order))
                    .ThenInclude(card => card.AssignedUser)
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await HasReadAccess(board, userId))
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        var canEdit = userId != null && await HasEditAccess(board, userId);

        var now = DateTime.UtcNow;
        var dotIndex = 0;
        var boardData = new BoardData
        {
            Id = board.Id,
            Name = board.Name,
            CanEdit = canEdit,
            Columns = board.Columns.OrderBy(c => c.Order).Select(col =>
            {
                var dotClass = DotColors[dotIndex % DotColors.Length];
                dotIndex++;
                return new ColumnData
                {
                    Id = col.Id,
                    Name = col.Name,
                    Color = dotClass,
                    DotClass = dotClass,
                    Status = col.ColumnStatus.ToString(),
                    Order = col.Order,
                    Cards = col.Cards.OrderBy(c => c.Order).Select(card => new CardSummary
                    {
                        Id = card.Id,
                        Title = card.Title,
                        Priority = card.Priority.ToString(),
                        DueDate = card.DueDate?.ToString("yyyy-MM-dd"),
                        IsOverdue = card.DueDate.HasValue && card.DueDate.Value < now
                            && col.ColumnStatus != ColumnStatus.Completed,
                        PlannedStartDate = card.PlannedStartTime?.ToString("yyyy-MM-dd"),
                        Assignee = card.AssignedUser != null ? new UserSummary
                        {
                            UserId = card.AssignedUser.Id,
                            DisplayName = card.AssignedUser.DisplayName ?? card.AssignedUser.UserName ?? string.Empty
                        } : null,
                        Labels = card.CardLabels.OrderBy(link => link.Label.Name).Select(link => new LabelSummary
                        {
                            Id = link.LabelId,
                            Name = link.Label.Name,
                            Color = link.Label.Color
                        }).ToList(),
                        CommentCount = 0,
                        IsRecurring = card.RecurrenceInterval.HasValue && card.RecurrenceUnit != RecurrenceUnit.None,
                        Description = card.Description
                    }).ToList()
                };
            }).ToList()
        };

        return this.StackView(new PublicBoardViewModel(board.Name)
        {
            Board = board,
            CanEdit = canEdit,
            BoardData = boardData
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
