using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.Notifications;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class BoardManagementApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanApiAccessService access,
    IAuthorizationService authorizationService,
    IMediator mediator,
    ILogger<BoardManagementApiController> logger) : ControllerBase
{
    [HttpGet("boards/{boardId:int}/gantt")]
    public async Task<IActionResult> Gantt(int boardId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null || !await access.CanReadAsync(board, CurrentUserId()))
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        var cards = await db.KanbanCards
            .Where(card => card.Column.BoardId == boardId)
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .Include(card => card.AssignedUser)
            .Include(card => card.CardLabels)
                .ThenInclude(link => link.Label)
            .OrderBy(card => card.Column.Order)
                .ThenBy(card => card.Order)
            .ToListAsync();
        return this.Protocol(new GanttResponse
        {
            Code = Code.ResultShown,
            Message = "Gantt chart data.",
            BoardId = board.Id,
            BoardName = board.Name,
            Cards = cards.Select(MobileApiMapper.ToTaskDto).ToList()
        });
    }

    [HttpPut("boards/{boardId:int}")]
    public async Task<IActionResult> UpdateBoard(int boardId, [FromBody] UpdateBoardRequest request)
    {
        var board = await LoadBoardAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        var userId = CurrentUserId();
        if (board.UserId != userId)
        {
            return this.Protocol(Code.Unauthorized, "Only the board owner can update board settings.");
        }
        if (request.Name == null && request.Order == null)
        {
            return this.Protocol(Code.InvalidInput, "Provide a board name or sort order.");
        }
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
        {
            return this.Protocol(Code.InvalidInput, "Board name is required.");
        }

        var oldName = board.Name;
        var oldOrder = board.Order;
        if (request.Name != null)
        {
            board.Name = request.Name.Trim();
        }
        if (request.Order.HasValue)
        {
            board.Order = request.Order.Value;
        }
        await db.SaveChangesAsync();

        if (!string.Equals(oldName, board.Name, StringComparison.Ordinal))
        {
            await PublishSafelyAsync(new BoardRenamedEvent(board.Id, oldName, board.Name, userId));
        }
        if (oldOrder != board.Order)
        {
            await PublishSafelyAsync(new BoardMovedEvent(board.Id, board.Name, oldOrder, board.Order, userId));
        }

        return this.Protocol(new BoardResponse
        {
            Code = Code.JobDone,
            Message = "Board updated.",
            Board = await ToDtoAsync(board, userId)
        });
    }

    [HttpDelete("boards/{boardId:int}")]
    public async Task<IActionResult> DeleteBoard(int boardId)
    {
        var board = await db.KanbanBoards
            .Include(item => item.Columns)
                .ThenInclude(column => column.Cards)
            .Include(item => item.BoardShares)
            .FirstOrDefaultAsync(item => item.Id == boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        var userId = CurrentUserId();
        if (board.UserId != userId)
        {
            return this.Protocol(Code.Unauthorized, "Only the board owner can delete this board.");
        }

        var boardName = board.Name;
        db.KanbanCards.RemoveRange(board.Columns.SelectMany(column => column.Cards));
        db.KanbanColumns.RemoveRange(board.Columns);
        db.BoardShares.RemoveRange(board.BoardShares);
        db.KanbanBoards.Remove(board);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new BoardDeletedEvent(boardId, boardName, userId));
        return this.Protocol(Code.JobDone, "Board deleted.");
    }

    [HttpPut("columns/{columnId:int}")]
    public async Task<IActionResult> UpdateColumn(int columnId, [FromBody] UpdateColumnRequest request)
    {
        var column = await db.KanbanColumns
            .Include(item => item.Board)
            .FirstOrDefaultAsync(item => item.Id == columnId);
        if (column == null)
        {
            return this.Protocol(Code.NotFound, "Column not found.");
        }
        var userId = CurrentUserId();
        if (!await access.CanEditAsync(column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }
        if (request.Name == null && request.Status == null)
        {
            return this.Protocol(Code.InvalidInput, "Provide a column name or status.");
        }
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
        {
            return this.Protocol(Code.InvalidInput, "Column name is required.");
        }
        if (request.Status != null && !Enum.TryParse<ColumnStatus>(request.Status, out _))
        {
            return this.Protocol(Code.InvalidInput, "Invalid column status.");
        }

        var oldName = column.Name;
        var oldStatus = column.ColumnStatus;
        if (request.Name != null)
        {
            column.Name = request.Name.Trim();
        }
        if (request.Status != null)
        {
            column.ColumnStatus = Enum.Parse<ColumnStatus>(request.Status);
        }
        await db.SaveChangesAsync();

        if (!string.Equals(oldName, column.Name, StringComparison.Ordinal))
        {
            await PublishSafelyAsync(new ColumnRenamedEvent(
                column.Id, oldName, column.Name, column.BoardId, userId));
        }
        if (oldStatus != column.ColumnStatus)
        {
            await PublishSafelyAsync(new ColumnStatusUpdatedEvent(
                column.Id,
                column.Name,
                (int)oldStatus,
                (int)column.ColumnStatus,
                column.BoardId,
                userId));
        }
        return await BoardResultAsync(column.BoardId, userId, "Column updated.");
    }

    [HttpPut("columns/{columnId:int}/position")]
    public async Task<IActionResult> MoveColumn(int columnId, [FromBody] MoveColumnRequest request)
    {
        var column = await db.KanbanColumns
            .Include(item => item.Board)
            .FirstOrDefaultAsync(item => item.Id == columnId);
        if (column == null)
        {
            return this.Protocol(Code.NotFound, "Column not found.");
        }
        var userId = CurrentUserId();
        if (!await access.CanEditAsync(column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var otherColumns = await db.KanbanColumns
            .Where(item => item.BoardId == column.BoardId && item.Id != column.Id)
            .OrderBy(item => item.Order)
            .ToListAsync();
        var oldOrder = column.Order;
        var newOrder = Math.Min(request.NewOrder, otherColumns.Count);
        otherColumns.Insert(newOrder, column);
        for (var index = 0; index < otherColumns.Count; index++)
        {
            otherColumns[index].Order = index;
        }
        await db.SaveChangesAsync();
        if (oldOrder != column.Order)
        {
            await PublishSafelyAsync(new ColumnMovedEvent(
                column.Id, column.Name, column.BoardId, oldOrder, column.Order, userId));
        }
        return await BoardResultAsync(column.BoardId, userId, "Column moved.");
    }

    [HttpDelete("columns/{columnId:int}")]
    public async Task<IActionResult> DeleteColumn(int columnId)
    {
        var column = await db.KanbanColumns
            .Include(item => item.Cards)
            .Include(item => item.Board)
            .FirstOrDefaultAsync(item => item.Id == columnId);
        if (column == null)
        {
            return this.Protocol(Code.NotFound, "Column not found.");
        }
        var userId = CurrentUserId();
        if (!await access.CanEditAsync(column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var boardId = column.BoardId;
        var columnName = column.Name;
        db.KanbanCards.RemoveRange(column.Cards);
        db.KanbanColumns.Remove(column);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new ColumnDeletedEvent(columnId, columnName, boardId, userId));
        return await BoardResultAsync(boardId, userId, "Column deleted.");
    }

    [HttpGet("boards/{boardId:int}/sharing")]
    public async Task<IActionResult> Sharing(int boardId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (!await CanManageSharesAsync(board))
        {
            return this.Protocol(Code.Unauthorized, "You cannot manage shares for this board.");
        }
        return this.Protocol(await BuildSharingResponseAsync(board, "Board sharing settings."));
    }

    [HttpPut("boards/{boardId:int}/visibility")]
    public async Task<IActionResult> SetVisibility(
        int boardId,
        [FromBody] UpdateBoardVisibilityRequest request)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (!await CanManageSharesAsync(board))
        {
            return this.Protocol(Code.Unauthorized, "You cannot manage shares for this board.");
        }

        board.IsPublic = request.IsPublic;
        await db.SaveChangesAsync();
        if (!request.IsPublic)
        {
            await CardSubscriptionService.RemoveSubscriptionsWithoutBoardAccessAsync(db, board.Id);
            await db.SaveChangesAsync();
        }
        return this.Protocol(await BuildSharingResponseAsync(
            board, request.IsPublic ? "Board is public." : "Board is private."));
    }

    [HttpPost("boards/{boardId:int}/shares")]
    public async Task<IActionResult> AddShare(int boardId, [FromBody] AddBoardShareRequest request)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (!await CanManageSharesAsync(board))
        {
            return this.Protocol(Code.Unauthorized, "You cannot manage shares for this board.");
        }

        var targetUserId = NormalizeId(request.TargetUserId);
        var targetRoleId = NormalizeId(request.TargetRoleId);
        if ((targetUserId == null) == (targetRoleId == null))
        {
            return this.Protocol(Code.InvalidInput, "Choose exactly one user or role.");
        }
        if (!Enum.TryParse<SharePermission>(request.Permission, out var permission))
        {
            return this.Protocol(Code.InvalidInput, "Invalid share permission.");
        }
        var userId = CurrentUserId();
        if (targetUserId != null)
        {
            if (targetUserId == userId)
            {
                return this.Protocol(Code.InvalidInput, "You cannot share a board with yourself.");
            }
            if (!await db.Users.AnyAsync(user => user.Id == targetUserId))
            {
                return this.Protocol(Code.NotFound, "User not found.");
            }
        }
        if (targetRoleId != null && !await db.Roles.AnyAsync(role => role.Id == targetRoleId))
        {
            return this.Protocol(Code.NotFound, "Role not found.");
        }
        var exists = await db.BoardShares.AnyAsync(share =>
            share.BoardId == boardId &&
            ((targetUserId != null && share.SharedWithUserId == targetUserId) ||
             (targetRoleId != null && share.SharedWithRoleId == targetRoleId)));
        if (exists)
        {
            return this.Protocol(Code.InvalidInput, "This user or role already has access.");
        }

        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            SharedWithUserId = targetUserId,
            SharedWithRoleId = targetRoleId,
            Permission = permission
        });
        await db.SaveChangesAsync();
        if (targetUserId != null)
        {
            await PublishSafelyAsync(new BoardSharedEvent(boardId, userId, targetUserId));
        }
        return this.Protocol(await BuildSharingResponseAsync(board, "Board share added."));
    }

    [HttpDelete("boards/{boardId:int}/shares/{shareId:guid}")]
    public async Task<IActionResult> RemoveShare(int boardId, Guid shareId)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (!await CanManageSharesAsync(board))
        {
            return this.Protocol(Code.Unauthorized, "You cannot manage shares for this board.");
        }
        var share = await db.BoardShares
            .FirstOrDefaultAsync(item => item.Id == shareId && item.BoardId == boardId);
        if (share == null)
        {
            return this.Protocol(Code.NotFound, "Board share not found.");
        }

        db.BoardShares.Remove(share);
        await db.SaveChangesAsync();
        await CardSubscriptionService.RemoveSubscriptionsWithoutBoardAccessAsync(db, boardId);
        await db.SaveChangesAsync();
        return this.Protocol(await BuildSharingResponseAsync(board, "Board share removed."));
    }

    private async Task<BoardSharingResponse> BuildSharingResponseAsync(KanbanBoard board, string message)
    {
        var userId = CurrentUserId();
        var shares = await db.BoardShares
            .Where(share => share.BoardId == board.Id)
            .Include(share => share.SharedWithUser)
            .OrderByDescending(share => share.CreationTime)
            .ToListAsync();
        var roleIds = shares
            .Where(share => share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .Distinct()
            .ToList();
        var roleNames = await db.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name ?? role.Id);
        var availableUsers = await db.Users
            .Where(user => user.Id != userId)
            .OrderBy(user => user.DisplayName)
            .Select(user => new ShareTargetDto
            {
                Id = user.Id,
                Name = user.UserName == null
                    ? user.DisplayName
                    : user.DisplayName + " (" + user.UserName + ")"
            })
            .ToListAsync();
        var availableRoles = await db.Roles
            .OrderBy(role => role.Name)
            .Select(role => new ShareTargetDto
            {
                Id = role.Id,
                Name = role.Name ?? role.Id
            })
            .ToListAsync();

        return new BoardSharingResponse
        {
            Code = Code.ResultShown,
            Message = message,
            BoardId = board.Id,
            BoardName = board.Name,
            IsPublic = board.IsPublic,
            PublicUrl = Url.Action("View", "PublicKanban", new { boardId = board.Id }, Request.Scheme) ?? string.Empty,
            Shares = shares.Select(share => new BoardShareDto
            {
                Id = share.Id,
                TargetId = share.SharedWithUserId ?? share.SharedWithRoleId ?? string.Empty,
                TargetName = share.SharedWithUserId != null
                    ? DisplayName(share.SharedWithUser)
                    : roleNames.GetValueOrDefault(share.SharedWithRoleId ?? string.Empty, share.SharedWithRoleId ?? string.Empty),
                TargetType = share.SharedWithUserId != null ? "User" : "Role",
                Permission = share.Permission.ToString(),
                CreationTime = share.CreationTime
            }).ToList(),
            AvailableUsers = availableUsers,
            AvailableRoles = availableRoles
        };
    }

    private async Task<bool> CanManageSharesAsync(KanbanBoard board) =>
        board.UserId == CurrentUserId() ||
        (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyBoardShare)).Succeeded;

    private async Task<IActionResult> BoardResultAsync(int boardId, string userId, string message)
    {
        var board = await LoadBoardAsync(boardId);
        return this.Protocol(new BoardResponse
        {
            Code = Code.JobDone,
            Message = message,
            Board = await ToDtoAsync(board!, userId)
        });
    }

    private Task<KanbanBoard?> LoadBoardAsync(int boardId) => db.KanbanBoards
        .Include(board => board.Columns.OrderBy(column => column.Order))
            .ThenInclude(column => column.Cards.OrderBy(card => card.Order))
        .FirstOrDefaultAsync(board => board.Id == boardId);

    private async Task<BoardDto> ToDtoAsync(KanbanBoard board, string userId) => new()
    {
        Id = board.Id,
        Name = board.Name,
        IsOwner = board.UserId == userId,
        CanEdit = await access.CanEditAsync(board, userId),
        IsArchived = board.IsArchived,
        ArchivedTime = board.ArchivedTime,
        Order = board.Order,
        IsPublic = board.IsPublic,
        ColumnCount = board.Columns.Count,
        CardCount = board.Columns.Sum(column => column.Cards.Count),
        Columns = board.Columns.OrderBy(column => column.Order).Select(column => new ColumnDto
        {
            Id = column.Id,
            Name = column.Name,
            Order = column.Order,
            Status = column.ColumnStatus.ToString(),
            Cards = column.Cards.OrderBy(card => card.Order).Select(card => new CardDto
            {
                Id = card.Id,
                ColumnId = card.ColumnId,
                Title = card.Title,
                Description = card.Description,
                Order = card.Order,
                Priority = card.Priority.ToString(),
                DueDate = card.DueDate,
                CreationTime = card.CreationTime
            }).ToList()
        }).ToList()
    };

    private async Task PublishSafelyAsync<TNotification>(TNotification notification)
        where TNotification : INotification
    {
        try
        {
            await mediator.Publish(notification);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to publish mobile API event {EventType}.",
                typeof(TNotification).Name);
        }
    }

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DisplayName(User? user) => user == null
        ? "Unknown user"
        : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Id
            : user.DisplayName;
}
