using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1")]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class KanbanApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanApiAccessService access,
    IOptions<AppSettings> appSettings,
    IMediator mediator,
    ILogger<KanbanApiController> logger) : ControllerBase
{
    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult Configuration()
    {
        var settings = appSettings.Value;
        var scopes = new List<string> { "openid", "profile", "email", "offline_access" };
        if (!string.IsNullOrWhiteSpace(settings.OIDC.ApiScope))
        {
            scopes.Add(settings.OIDC.ApiScope);
        }
        return this.Protocol(new MobileConfigurationResponse
        {
            Code = Code.ResultShown,
            Message = "Mobile client configuration.",
            AuthenticationMode = settings.AuthProvider,
            AllowRegistration = settings.LocalEnabled && settings.Local.AllowRegister,
            Authority = settings.OIDCEnabled ? settings.OIDC.GetMobileAuthority() : string.Empty,
            ClientId = settings.OIDCEnabled ? settings.OIDC.MobileClientId : string.Empty,
            RedirectUri = settings.OIDCEnabled ? settings.OIDC.MobileRedirectUri : string.Empty,
            Scopes = settings.OIDCEnabled ? scopes : []
        });
    }

    [HttpGet("boards")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> Boards()
    {
        var userId = CurrentUserId();
        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();

        var boards = await db.KanbanBoards
            .Where(board => !board.IsArchived &&
                (board.UserId == userId || board.BoardShares.Any(share =>
                    share.SharedWithUserId == userId ||
                    (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId)))))
            .Include(board => board.Columns)
                .ThenInclude(column => column.Cards)
            .OrderBy(board => board.Order)
            .ToListAsync();

        var result = new List<BoardSummaryDto>();
        foreach (var board in boards)
        {
            result.Add(new BoardSummaryDto
            {
                Id = board.Id,
                Name = board.Name,
                IsOwner = board.UserId == userId,
                CanEdit = await access.CanEditAsync(board, userId),
                ColumnCount = board.Columns.Count,
                CardCount = board.Columns.Sum(column => column.Cards.Count)
            });
        }

        return this.Protocol(new BoardListResponse
        {
            Code = Code.ResultShown,
            Message = "Accessible boards.",
            Boards = result
        });
    }

    [HttpGet("boards/{boardId:int}")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> Board(int boardId)
    {
        var userId = CurrentUserId();
        var board = await LoadBoardAsync(boardId);
        if (board == null || !await access.CanReadAsync(board, userId))
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }

        return this.Protocol(new BoardResponse
        {
            Code = Code.ResultShown,
            Message = "Board loaded.",
            Board = await ToDtoAsync(board, userId)
        });
    }

    [HttpGet("boards/archived")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> ArchivedBoards()
    {
        var userId = CurrentUserId();
        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        var ownedBoards = await db.KanbanBoards
            .Where(board => board.UserId == userId && board.IsArchived)
            .Include(board => board.Columns)
                .ThenInclude(column => column.Cards)
            .OrderByDescending(board => board.ArchivedTime)
            .ToListAsync();
        var sharedBoardShares = await db.BoardShares
            .Where(share => share.Board.IsArchived && share.Board.UserId != userId &&
                (share.SharedWithUserId == userId ||
                 (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId))))
            .Include(share => share.Board)
                .ThenInclude(board => board.Columns)
                    .ThenInclude(column => column.Cards)
            .OrderByDescending(share => share.CreationTime)
            .ToListAsync();
        var sharedRoleIds = sharedBoardShares
            .Where(share => share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .Distinct()
            .ToList();
        var roleNames = await db.Roles
            .Where(role => sharedRoleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name ?? role.Id);
        var now = DateTime.UtcNow;
        var sharedBoards = sharedBoardShares
            .GroupBy(share => share.BoardId)
            .Select(group => group
                .OrderByDescending(share => share.Permission)
                .ThenByDescending(share => share.CreationTime)
                .First())
            .Select(share => ToArchivedDto(
                share.Board,
                isOwner: false,
                now,
                share.Permission.ToString(),
                share.SharedWithUserId == userId
                    ? "Direct share"
                    : $"Role: {roleNames.GetValueOrDefault(share.SharedWithRoleId!, share.SharedWithRoleId!)}"))
            .OrderBy(board => board.Name)
            .ToList();

        return this.Protocol(new ArchivedBoardListResponse
        {
            Code = Code.ResultShown,
            Message = "Archived boards.",
            OwnedBoards = ownedBoards
                .Select(board => ToArchivedDto(board, isOwner: true, now))
                .ToList(),
            SharedBoards = sharedBoards
        });
    }

    [HttpPut("boards/{boardId:int}/archive")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> SetArchived(int boardId, [FromBody] SetBoardArchiveRequest request)
    {
        var userId = CurrentUserId();
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (board.UserId != userId)
        {
            return this.Protocol(Code.Unauthorized, "Only the board owner can change archive state.");
        }

        var changed = board.IsArchived != request.Archive;
        if (changed)
        {
            board.IsArchived = request.Archive;
            board.ArchivedTime = request.Archive ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
        }
        return this.Protocol(new BoardArchiveResponse
        {
            Code = changed ? Code.JobDone : Code.NoActionTaken,
            Message = request.Archive ? "Board archived." : "Board restored.",
            BoardId = board.Id,
            IsArchived = board.IsArchived,
            ArchivedTime = board.ArchivedTime
        });
    }

    [HttpPost("boards")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> CreateBoard([FromBody] CreateBoardRequest request)
    {
        var userId = CurrentUserId();
        var maxOrder = await db.KanbanBoards
            .Where(board => board.UserId == userId)
            .MaxAsync(board => (int?)board.Order) ?? 0;
        var board = new KanbanBoard
        {
            Name = request.Name.Trim(),
            UserId = userId,
            Order = maxOrder + 100,
            Columns =
            [
                new KanbanColumn { Name = "To Do", Order = 0, ColumnStatus = ColumnStatus.NotStarted },
                new KanbanColumn { Name = "In Progress", Order = 1, ColumnStatus = ColumnStatus.InProgress },
                new KanbanColumn { Name = "Done", Order = 2, ColumnStatus = ColumnStatus.Completed }
            ]
        };
        db.KanbanBoards.Add(board);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new BoardCreatedEvent(board.Id, board.Name, userId));

        return this.Protocol(new BoardResponse
        {
            Code = Code.JobDone,
            Message = "Board created.",
            Board = await ToDtoAsync(board, userId)
        });
    }

    [HttpPost("boards/{boardId:int}/columns")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> CreateColumn(int boardId, [FromBody] CreateColumnRequest request)
    {
        var userId = CurrentUserId();
        var board = await LoadBoardAsync(boardId);
        if (board == null)
        {
            return this.Protocol(Code.NotFound, "Board not found.");
        }
        if (!await access.CanEditAsync(board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var column = new KanbanColumn
        {
            BoardId = board.Id,
            Name = request.Name.Trim(),
            Order = board.Columns.Count == 0 ? 0 : board.Columns.Max(item => item.Order) + 1
        };
        db.KanbanColumns.Add(column);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new ColumnCreatedEvent(
            column.Id, column.Name, column.BoardId, userId));
        board = (await LoadBoardAsync(board.Id))!;

        return this.Protocol(new BoardResponse
        {
            Code = Code.JobDone,
            Message = "Column created.",
            Board = await ToDtoAsync(board, userId)
        });
    }

    [HttpPost("columns/{columnId:int}/cards")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> CreateCard(int columnId, [FromBody] CreateCardRequest request)
    {
        var userId = CurrentUserId();
        var column = await db.KanbanColumns
            .Include(item => item.Board)
            .Include(item => item.Cards)
            .FirstOrDefaultAsync(item => item.Id == columnId);
        if (column == null)
        {
            return this.Protocol(Code.NotFound, "Column not found.");
        }
        if (!await access.CanEditAsync(column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var card = new KanbanCard
        {
            ColumnId = column.Id,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Order = column.Cards.Count == 0 ? 0 : column.Cards.Max(item => item.Order) + 1,
            CreatorUserId = userId,
            AssignedUserId = userId
        };
        card.Subscriptions.Add(new KanbanCardSubscription { Card = card, UserId = userId });
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new CardCreatedEvent(
            card.Id, card.Title, column.Id, column.BoardId, userId));

        return this.Protocol(new CardResponse
        {
            Code = Code.JobDone,
            Message = "Card created.",
            Card = ToDto(card)
        });
    }

    [HttpPut("cards/{cardId:int}/position")]
    [Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
    public async Task<IActionResult> MoveCard(int cardId, [FromBody] MoveCardRequest request)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        var target = await db.KanbanColumns
            .Include(column => column.Board)
            .FirstOrDefaultAsync(column => column.Id == request.TargetColumnId);
        if (card == null || target == null)
        {
            return this.Protocol(Code.NotFound, "Card or target column not found.");
        }
        if (card.Column.BoardId != target.BoardId)
        {
            return this.Protocol(Code.InvalidInput, "Target column must belong to the same board.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var fromColumnId = card.ColumnId;
        var fromColumnName = card.Column.Name;
        var now = DateTime.UtcNow;
        var wasCompleted = card.Column.ColumnStatus == ColumnStatus.Completed;
        switch (target.ColumnStatus)
        {
            case ColumnStatus.InProgress:
                card.ActualStartTime ??= now;
                card.ActualEndTime = null;
                break;
            case ColumnStatus.Completed:
                card.ActualStartTime ??= now;
                card.ActualEndTime = now;
                break;
        }

        var shouldRecur = target.ColumnStatus == ColumnStatus.Completed &&
            !wasCompleted &&
            card.RecurrenceInterval is > 0 &&
            card.RecurrenceUnit != RecurrenceUnit.None;
        KanbanColumn? recurrenceTargetColumn = null;
        if (shouldRecur)
        {
            var baseline = card.DueDate ?? now;
            card.DueDate = AdvanceByRecurrence(
                baseline, card.RecurrenceInterval!.Value, card.RecurrenceUnit);
            if (card.PlannedStartTime.HasValue)
            {
                card.PlannedStartTime = AdvanceByRecurrence(
                    card.PlannedStartTime.Value,
                    card.RecurrenceInterval.Value,
                    card.RecurrenceUnit);
            }
            recurrenceTargetColumn = await db.KanbanColumns
                .Where(column => column.BoardId == target.BoardId &&
                    column.ColumnStatus == ColumnStatus.NotStarted)
                .OrderBy(column => column.Order)
                .FirstOrDefaultAsync();
            if (recurrenceTargetColumn == null)
            {
                shouldRecur = false;
            }
            else
            {
                card.ActualStartTime = null;
                card.ActualEndTime = null;
            }
        }

        var cards = await db.KanbanCards
            .Where(item => item.ColumnId == target.Id && item.Id != card.Id)
            .OrderBy(item => item.Order)
            .ToListAsync();
        var insertionIndex = Math.Min(request.NewOrder, cards.Count);
        cards.Insert(insertionIndex, card);
        for (var index = 0; index < cards.Count; index++)
        {
            cards[index].ColumnId = target.Id;
            cards[index].Order = index;
        }

        if (shouldRecur && recurrenceTargetColumn != null)
        {
            card.ColumnId = recurrenceTargetColumn.Id;
            var destinationCards = await db.KanbanCards
                .Where(item => item.ColumnId == recurrenceTargetColumn.Id && item.Id != card.Id)
                .OrderBy(item => item.Order)
                .ToListAsync();
            for (var index = 0; index < destinationCards.Count; index++)
            {
                destinationCards[index].Order = index;
            }
            card.Order = destinationCards.Count;

            var completedCards = await db.KanbanCards
                .Where(item => item.ColumnId == target.Id && item.Id != card.Id)
                .OrderBy(item => item.Order)
                .ToListAsync();
            for (var index = 0; index < completedCards.Count; index++)
            {
                completedCards[index].Order = index;
            }
        }
        card.LastUpdatedAt = now;
        await db.SaveChangesAsync();

        if (fromColumnId != card.ColumnId)
        {
            await PublishSafelyAsync(new CardMovedEvent(
                card.Id,
                userId,
                fromColumnId,
                fromColumnName,
                card.ColumnId,
                shouldRecur ? recurrenceTargetColumn!.Name : target.Name,
                card.Order,
                NotifyUsers: !shouldRecur));
        }

        return this.Protocol(new CardResponse
        {
            Code = Code.JobDone,
            Message = shouldRecur
                ? $"Recurring card reset to {recurrenceTargetColumn!.Name}."
                : "Card moved.",
            Card = ToDto(card)
        });
    }

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
                "Failed to publish mobile API notification event {EventType}.",
                typeof(TNotification).Name);
        }
    }

    private static DateTime AdvanceByRecurrence(DateTime baseline, int interval, RecurrenceUnit unit) =>
        unit switch
        {
            RecurrenceUnit.Day => baseline.AddDays(interval),
            RecurrenceUnit.Week => baseline.AddDays(7 * interval),
            RecurrenceUnit.Month => baseline.AddMonths(interval),
            RecurrenceUnit.Year => baseline.AddYears(interval),
            _ => baseline
        };

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private Task<KanbanBoard?> LoadBoardAsync(int boardId) => db.KanbanBoards
        .Include(board => board.Columns)
            .ThenInclude(column => column.Cards)
                .ThenInclude(card => card.AssignedUser)
        .Include(board => board.Columns)
            .ThenInclude(column => column.Cards)
                .ThenInclude(card => card.CardLabels)
                    .ThenInclude(link => link.Label)
        .FirstOrDefaultAsync(board => board.Id == boardId);

    private static ArchivedBoardDto ToArchivedDto(
        KanbanBoard board,
        bool isOwner,
        DateTime now,
        string? permission = null,
        string? sharedVia = null)
    {
        var cards = board.Columns.SelectMany(column => column.Cards).ToList();
        return new ArchivedBoardDto
        {
            Id = board.Id,
            Name = board.Name,
            IsOwner = isOwner,
            ArchivedTime = board.ArchivedTime,
            ColumnCount = board.Columns.Count,
            CardCount = cards.Count,
            IncompleteCount = cards.Count(card => card.Column.ColumnStatus != ColumnStatus.Completed),
            InProgressCount = cards.Count(card => card.Column.ColumnStatus == ColumnStatus.InProgress),
            CompletedCount = cards.Count(card => card.Column.ColumnStatus == ColumnStatus.Completed),
            OverdueCount = cards.Count(card => card.DueDate.HasValue && card.DueDate.Value < now &&
                card.Column.ColumnStatus != ColumnStatus.Completed),
            UnassignedCount = cards.Count(card => string.IsNullOrEmpty(card.AssignedUserId)),
            Permission = permission,
            SharedVia = sharedVia
        };
    }

    private async Task<BoardDto> ToDtoAsync(KanbanBoard board, string userId)
    {
        var cardIds = board.Columns.SelectMany(column => column.Cards).Select(card => card.Id).ToList();
        var commentCardIds = await db.KanbanCardComments
            .Where(comment => cardIds.Contains(comment.CardId))
            .Select(comment => comment.CardId)
            .ToListAsync();
        var commentCounts = commentCardIds
            .GroupBy(cardId => cardId)
            .ToDictionary(group => group.Key, group => group.Count());
        return new BoardDto
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
                Cards = column.Cards
                    .OrderBy(card => card.Order)
                    .Select(card => ToDto(card, commentCounts.GetValueOrDefault(card.Id)))
                    .ToList()
            }).ToList()
        };
    }

    private static CardDto ToDto(KanbanCard card, int commentCount = 0) => new()
    {
        Id = card.Id,
        ColumnId = card.ColumnId,
        Title = card.Title,
        Description = card.Description,
        Order = card.Order,
        Priority = card.Priority.ToString(),
        PlannedStartTime = card.PlannedStartTime,
        DueDate = card.DueDate,
        ActualStartTime = card.ActualStartTime,
        ActualEndTime = card.ActualEndTime,
        RecurrenceInterval = card.RecurrenceInterval,
        RecurrenceUnit = card.RecurrenceUnit.ToString(),
        CreationTime = card.CreationTime,
        AssignedUser = card.AssignedUser == null ? null : MobileApiMapper.ToUserDto(card.AssignedUser),
        Labels = card.CardLabels
            .OrderBy(link => link.Label.Name)
            .Select(link => new CardLabelDto
            {
                Id = link.LabelId,
                Name = link.Label.Name,
                Color = link.Label.Color
            })
            .ToList(),
        CommentCount = commentCount
    };
}
