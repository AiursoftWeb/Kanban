// ============================================================
// CardsController — Card detail page (GET /Cards/{id})
// Field updates are handled via fetch to existing KanbanController endpoints
// ============================================================

using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.CardViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
public class CardsController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storage,
    ILogger<CardsController> logger) : Controller
{
    /// <summary>
    /// GET /Cards/{id}?returnBoardId=X
    /// Shows the full card detail page.
    /// </summary>
    [HttpGet("/Cards/{id:int}")]
    public async Task<IActionResult> Detail(int id, [FromQuery] int? returnBoardId)
    {
        var userId = userManager.GetUserId(User)!;

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .Include(c => c.CardLabels)
                .ThenInclude(link => link.Label)
            .Include(c => c.AssignedUser)
            .Include(c => c.CreatorUser)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (card == null)
            return NotFound();

        var board = card.Column.Board;

        // Check access
        if (board.UserId != userId)
        {
            var hasAccess = await HasSharedAccess(board.Id, userId);
            if (!hasAccess)
                return NotFound();
        }

        var canEdit = board.UserId == userId;

        // Build comments
        var comments = await db.KanbanCardComments
            .Where(c => c.CardId == id)
            .OrderBy(c => c.CreationTime)
            .Include(c => c.Author)
            .ToListAsync();

        var commentVms = comments.Select(c => new CommentViewModel
        {
            Id = c.Id,
            Content = c.Content,
            AuthorName = c.Author?.DisplayName ?? c.Author?.UserName ?? "Unknown",
            AuthorInitial = (c.Author?.DisplayName ?? c.Author?.UserName ?? "?")[0].ToString().ToUpperInvariant(),
            AuthorAvatarUrl = c.Author?.AvatarRelativePath != null
                && c.Author.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(c.Author.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreatedAt = c.CreationTime.ToString("yyyy-MM-dd HH:mm"),
            CanDelete = c.AuthorId == userId || canEdit
        }).ToList();

        var model = new CardDetailViewModel
        {
            CardId = card.Id,
            Title = card.Title,
            Description = card.Description ?? string.Empty,
            Priority = (int)card.Priority,
            ColumnId = card.ColumnId,
            ColumnName = card.Column.Name,
            BoardId = board.Id,
            BoardName = board.Name,
            ReturnBoardId = returnBoardId ?? board.Id,
            CanEdit = canEdit,
            AssigneeId = card.AssignedUserId,
            AssigneeName = card.AssignedUser?.DisplayName ?? card.AssignedUser?.UserName ?? string.Empty,
            AssigneeInitial = string.IsNullOrEmpty(card.AssignedUser?.DisplayName ?? card.AssignedUser?.UserName)
                ? string.Empty
                : (card.AssignedUser!.DisplayName ?? card.AssignedUser.UserName!)![0].ToString().ToUpperInvariant(),
            AssigneeAvatarUrl = card.AssignedUser?.AvatarRelativePath != null
                && card.AssignedUser.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(card.AssignedUser.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreatorName = card.CreatorUser?.DisplayName ?? card.CreatorUser?.UserName ?? string.Empty,
            CreatorInitial = string.IsNullOrEmpty(card.CreatorUser?.DisplayName ?? card.CreatorUser?.UserName)
                ? string.Empty
                : (card.CreatorUser!.DisplayName ?? card.CreatorUser.UserName!)![0].ToString().ToUpperInvariant(),
            CreatorAvatarUrl = card.CreatorUser?.AvatarRelativePath != null
                && card.CreatorUser.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(card.CreatorUser.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreationTime = card.CreationTime.ToString("yyyy-MM-dd HH:mm"),
            DueDate = card.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            PlannedStartDate = card.PlannedStartTime?.ToString("yyyy-MM-dd") ?? string.Empty,
            ActualStartDate = card.ActualStartTime?.ToString("yyyy-MM-ddTHH:mm") ?? string.Empty,
            ActualEndDate = card.ActualEndTime?.ToString("yyyy-MM-ddTHH:mm") ?? string.Empty,
            IsRecurring = card.RecurrenceInterval.HasValue && card.RecurrenceUnit != RecurrenceUnit.None,
            RecurrenceInterval = card.RecurrenceInterval?.ToString() ?? string.Empty,
            RecurrenceUnit = (int)card.RecurrenceUnit,
            Labels = card.CardLabels
                .OrderBy(link => link.Label.Name)
                .Select(link => new LabelViewModel
                {
                    Id = link.LabelId,
                    Name = link.Label.Name,
                    Color = link.Label.Color
                }).ToList(),
            Comments = commentVms,
            Columns = await db.KanbanColumns
                .Where(c => c.BoardId == board.Id)
                .OrderBy(c => c.Order)
                .Select(c => new ColumnOptionViewModel
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync()
        };

        // Add available boards for transfer
        model.AvailableBoards = await db.KanbanBoards
            .Where(b => b.UserId == userId && b.Id != board.Id)
            .OrderBy(b => b.Name)
            .Select(b => new BoardOptionViewModel
            {
                Id = b.Id,
                Name = b.Name
            })
            .ToListAsync();

        return this.StackView(model);
    }

    private async Task<bool> HasSharedAccess(int boardId, string userId)
    {
        var share = await db.BoardShares
            .FirstOrDefaultAsync(s => s.BoardId == boardId && s.SharedWithUserId == userId);
        if (share != null) return true;

        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var roleShare = await db.BoardShares
            .FirstOrDefaultAsync(s => s.BoardId == boardId
                && s.SharedWithRoleId != null
                && userRoleIds.Contains(s.SharedWithRoleId));
        return roleShare != null;
    }
}
