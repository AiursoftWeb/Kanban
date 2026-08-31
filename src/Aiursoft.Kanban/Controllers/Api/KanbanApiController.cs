using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
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
    IOptions<AppSettings> appSettings) : ControllerBase
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

        var now = DateTime.UtcNow;
        if (target.ColumnStatus == ColumnStatus.InProgress)
        {
            card.ActualStartTime ??= now;
            card.ActualEndTime = null;
        }
        else if (target.ColumnStatus == ColumnStatus.Completed)
        {
            card.ActualStartTime ??= now;
            card.ActualEndTime = now;
        }
        card.LastUpdatedAt = now;
        await db.SaveChangesAsync();

        return this.Protocol(new CardResponse
        {
            Code = Code.JobDone,
            Message = "Card moved.",
            Card = ToDto(card)
        });
    }

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

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
        ColumnCount = board.Columns.Count,
        CardCount = board.Columns.Sum(column => column.Cards.Count),
        Columns = board.Columns.OrderBy(column => column.Order).Select(column => new ColumnDto
        {
            Id = column.Id,
            Name = column.Name,
            Order = column.Order,
            Status = column.ColumnStatus.ToString(),
            Cards = column.Cards.OrderBy(card => card.Order).Select(ToDto).ToList()
        }).ToList()
    };

    private static CardDto ToDto(KanbanCard card) => new()
    {
        Id = card.Id,
        ColumnId = card.ColumnId,
        Title = card.Title,
        Description = card.Description,
        Order = card.Order,
        Priority = card.Priority.ToString(),
        DueDate = card.DueDate,
        CreationTime = card.CreationTime
    };
}
