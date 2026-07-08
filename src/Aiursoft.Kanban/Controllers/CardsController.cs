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
    [HttpGet("/Cards/New")]
    public async Task<IActionResult> New([FromQuery] int columnId, [FromQuery] int? returnBoardId)
    {
        var userId = userManager.GetUserId(User)!;
        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null)
            return NotFound();

        if (!await HasEditAccess(column.Board, userId))
            return Forbid();

        var user = await userManager.FindByIdAsync(userId);
        var model = new CardDetailViewModel
        {
            IsNew = true,
            Title = string.Empty,
            Description = string.Empty,
            Priority = (int)Priority.None,
            ColumnId = column.Id,
            ColumnName = column.Name,
            BoardId = column.Board.Id,
            BoardName = column.Board.Name,
            ReturnBoardId = returnBoardId ?? column.Board.Id,
            CanEdit = true,
            AssigneeId = userId,
            AssigneeName = GetUserDisplayName(user) ?? string.Empty,
            AssigneeInitial = GetUserInitial(user),
            AssigneeAvatarUrl = GetUserAvatarUrl(user),
            CreatorName = GetUserDisplayName(user) ?? string.Empty,
            CreatorInitial = GetUserInitial(user),
            CreatorAvatarUrl = GetUserAvatarUrl(user),
            CreationTime = DateTime.UtcNow,
            Columns = await db.KanbanColumns
                .Where(c => c.BoardId == column.BoardId)
                .OrderBy(c => c.Order)
                .Select(c => new ColumnOptionViewModel
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync()
        };

        return this.StackView(model, "Detail");
    }

    [HttpPost("/Cards/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        int columnId,
        string? title,
        string? description,
        DateTime? plannedStartTime,
        DateTime? dueDate,
        int priority = (int)Priority.None,
        string? assignedUserId = null,
        int? recurrenceInterval = null,
        int recurrenceUnit = (int)RecurrenceUnit.None)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Title is required.");

        if (!Enum.IsDefined(typeof(Priority), priority))
            return BadRequest("Invalid priority.");

        if (!Enum.IsDefined(typeof(RecurrenceUnit), recurrenceUnit))
            return BadRequest("Invalid recurrence unit.");

        if (recurrenceInterval is < 0)
            return BadRequest("Recurrence interval cannot be negative.");

        if (recurrenceInterval is > 365)
            return BadRequest("Recurrence interval cannot exceed 365.");

        if (recurrenceInterval is > 0 && recurrenceUnit == (int)RecurrenceUnit.None)
            return BadRequest("Recurrence unit is required when recurrence interval is set.");

        if (recurrenceInterval is > 0 && dueDate == null)
            return BadRequest("Due date is required when recurrence is set.");

        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        var normalizedAssignedUserId = NormalizeAssignedUserId(assignedUserId);
        if (!await CanAssignUserToBoardAsync(column.Board, normalizedAssignedUserId))
            return BadRequest("Assigned user does not have access to this board.");

        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == columnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;
        var newRecurrenceInterval = recurrenceInterval is > 0 ? recurrenceInterval : null;

        var card = new KanbanCard
        {
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Order = maxOrder + 1,
            ColumnId = columnId,
            CreatorUserId = userId,
            AssignedUserId = normalizedAssignedUserId,
            PlannedStartTime = NormalizeDateTime(plannedStartTime),
            DueDate = NormalizeDateTime(dueDate),
            Priority = (Priority)priority,
            RecurrenceInterval = newRecurrenceInterval,
            RecurrenceUnit = newRecurrenceInterval.HasValue ? (RecurrenceUnit)recurrenceUnit : RecurrenceUnit.None
        };

        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();

        return Ok(new { card.Id });
    }

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
        if (board.IsArchived) return false;
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

    private async Task<HashSet<string>> GetAccessibleBoardUserIdsAsync(KanbanBoard board)
    {
        var accessibleUserIds = await db.BoardShares
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
        accessibleUserIds.UnionWith(roleUserIds);

        if (!string.IsNullOrWhiteSpace(board.UserId))
            accessibleUserIds.Add(board.UserId);

        return accessibleUserIds;
    }

    private async Task<bool> CanAssignUserToBoardAsync(KanbanBoard board, string? assignedUserId)
    {
        if (assignedUserId == null) return true;
        if (!await db.Users.AnyAsync(user => user.Id == assignedUserId)) return false;
        return (await GetAccessibleBoardUserIdsAsync(board)).Contains(assignedUserId);
    }

    private string? GetUserAvatarUrl(User? user)
    {
        if (user == null || user.AvatarRelativePath == Entities.User.DefaultAvatarPath)
            return null;

        return $"{storage.RelativePathToInternetUrl(user.AvatarRelativePath)}?w=56&square=true";
    }

    private static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }

    private static string GetUserInitial(User? user)
    {
        var displayName = GetUserDisplayName(user);
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim()[0].ToString().ToUpperInvariant();
    }

    private static string? NormalizeAssignedUserId(string? assignedUserId)
    {
        return string.IsNullOrWhiteSpace(assignedUserId) ? null : assignedUserId.Trim();
    }

    private static DateTime? NormalizeDateTime(DateTime? dt)
    {
        if (dt == null) return null;
        if (dt.Value.Kind == DateTimeKind.Utc) return dt;
        if (dt.Value.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);
        return dt.Value.ToUniversalTime();
    }
}
