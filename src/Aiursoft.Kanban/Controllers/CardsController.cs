// ============================================================
// CardsController — Card detail page (GET /Cards/{id})
// Field updates are handled via fetch to existing KanbanController endpoints
// ============================================================

using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.CardViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
public class CardsController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storage) : Controller
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
        if (!await HasReadAccess(board, userId))
        {
            return NotFound();
        }

        var canEdit = await HasEditAccess(board, userId);

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
            Images = c.Images,
            AuthorName = !string.IsNullOrEmpty(c.Author.DisplayName) ? c.Author.DisplayName : c.Author.UserName ?? "Unknown",
            AuthorInitial = (!string.IsNullOrEmpty(c.Author.DisplayName) ? c.Author.DisplayName : c.Author.UserName ?? "?")[0].ToString().ToUpperInvariant(),
            AuthorAvatarUrl = c.Author.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(c.Author.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreatedAt = c.CreationTime.ToString("yyyy-MM-ddTHH:mmK"),
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
            AssigneeInitial = (card.AssignedUser?.DisplayName ?? card.AssignedUser?.UserName) is { Length: > 0 } name
                ? name[0].ToString().ToUpperInvariant()
                : string.Empty,
            AssigneeAvatarUrl = card.AssignedUser != null && card.AssignedUser.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(card.AssignedUser.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreatorName = card.CreatorUser?.DisplayName ?? card.CreatorUser?.UserName ?? string.Empty,
            CreatorInitial = (card.CreatorUser?.DisplayName ?? card.CreatorUser?.UserName) is { Length: > 0 } name2
                ? name2[0].ToString().ToUpperInvariant()
                : string.Empty,
            CreatorAvatarUrl = card.CreatorUser != null && card.CreatorUser.AvatarRelativePath != Entities.User.DefaultAvatarPath
                ? $"{storage.RelativePathToInternetUrl(card.CreatorUser.AvatarRelativePath)}?w=56&square=true"
                : null,
            CreationTime = card.CreationTime,
            DueDate = card.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            PlannedStartDate = card.PlannedStartTime?.ToString("yyyy-MM-dd") ?? string.Empty,
            ActualStartDate = card.ActualStartTime,
            ActualEndDate = card.ActualEndTime,
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

        // Add available boards for transfer (owned or shared with Edit permission)
        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        model.AvailableBoards = await db.KanbanBoards
            .Where(b => b.Id != board.Id &&
                (b.UserId == userId ||
                 b.BoardShares.Any(s =>
                     s.Permission == SharePermission.Editable &&
                     (s.SharedWithUserId == userId ||
                      (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId))))))
            .OrderBy(b => b.Name)
            .Select(b => new BoardOptionViewModel
            {
                Id = b.Id,
                Name = b.Name
            })
            .ToListAsync();

        return this.StackView(model);
    }

    private async Task<bool> HasReadAccess(KanbanBoard board, string userId)
    {
        if (board.IsPublic) return true;
        if (board.UserId == userId) return true;

        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return await db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId))));
    }

    private async Task<bool> HasEditAccess(KanbanBoard board, string userId)
    {
        if (board.UserId == userId) return true;

        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return await db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            s.Permission == SharePermission.Editable &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId))));
    }
}
