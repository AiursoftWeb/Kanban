using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
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

[Route("api/v1/cards")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class CardApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanApiAccessService access,
    IMediator mediator,
    ILogger<CardApiController> logger) : ControllerBase
{
    private static readonly string[] LabelColors =
    [
        "#EF4444",
        "#F97316",
        "#EAB308",
        "#22C55E",
        "#3B82F6",
        "#8B5CF6",
        "#EC4899",
        "#14B8A6"
    ];

    [HttpGet("{cardId:int}")]
    public async Task<IActionResult> Details(int cardId)
    {
        var userId = CurrentUserId();
        var card = await LoadCardAsync(cardId);
        if (card == null || !await access.CanReadAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }

        return this.Protocol(new CardDetailsResponse
        {
            Code = Code.ResultShown,
            Message = "Card loaded.",
            Card = await ToDetailsDtoAsync(card, userId)
        });
    }

    [HttpPut("{cardId:int}")]
    public async Task<IActionResult> Update(int cardId, [FromBody] UpdateCardRequest request)
    {
        var userId = CurrentUserId();
        var card = await LoadCardAsync(cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }
        if (!Enum.TryParse<Priority>(request.Priority, true, out var priority) ||
            !Enum.IsDefined(priority))
        {
            return this.Protocol(Code.InvalidInput, "Invalid priority.");
        }
        if (!Enum.TryParse<RecurrenceUnit>(request.RecurrenceUnit, true, out var recurrenceUnit) ||
            !Enum.IsDefined(recurrenceUnit))
        {
            return this.Protocol(Code.InvalidInput, "Invalid recurrence unit.");
        }
        if (request.RecurrenceInterval.HasValue && recurrenceUnit == RecurrenceUnit.None)
        {
            return this.Protocol(Code.InvalidInput, "Recurrence unit is required when recurrence is enabled.");
        }
        if (request.RecurrenceInterval.HasValue && request.DueDate == null)
        {
            return this.Protocol(Code.InvalidInput, "Due date is required when recurrence is enabled.");
        }

        var assignedUserId = NormalizeUserId(request.AssignedUserId);
        if (!await access.CanAssignAsync(card.Column.Board, assignedUserId))
        {
            return this.Protocol(Code.InvalidInput, "Assigned user does not have access to this board.");
        }

        var title = request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var plannedStartTime = NormalizeDateTime(request.PlannedStartTime);
        var dueDate = NormalizeDateTime(request.DueDate);
        var changedFields = new List<string>();
        AddChangedField(changedFields, "title", card.Title != title);
        AddChangedField(changedFields, "description", card.Description != description);
        AddChangedField(changedFields, "planned start time", card.PlannedStartTime != plannedStartTime);
        AddChangedField(changedFields, "due date", card.DueDate != dueDate);
        AddChangedField(changedFields, "priority", card.Priority != priority);
        var normalizedRecurrenceUnit = request.RecurrenceInterval.HasValue
            ? recurrenceUnit
            : RecurrenceUnit.None;
        AddChangedField(changedFields, "recurrence",
            card.RecurrenceInterval != request.RecurrenceInterval || card.RecurrenceUnit != normalizedRecurrenceUnit);

        var oldAssigneeId = card.AssignedUserId;
        card.Title = title;
        card.Description = description;
        card.Priority = priority;
        card.AssignedUserId = assignedUserId;
        card.PlannedStartTime = plannedStartTime;
        card.DueDate = dueDate;
        card.RecurrenceInterval = request.RecurrenceInterval;
        card.RecurrenceUnit = normalizedRecurrenceUnit;
        await CardSubscriptionService.SubscribeAsync(db, card.Id, new[] { assignedUserId });
        await db.SaveChangesAsync();

        if (changedFields.Count > 0)
        {
            await PublishSafelyAsync(new CardUpdatedEvent(card.Id, userId, changedFields));
        }
        if (oldAssigneeId != assignedUserId)
        {
            await PublishSafelyAsync(new CardAssignedEvent(card.Id, userId, oldAssigneeId, assignedUserId));
        }
        if (description != null)
        {
            var mentionedUserIds = await MentionParser.ExtractMentionedUserIds(
                db, description, card.Column.BoardId, HttpContext.RequestAborted);
            if (mentionedUserIds.Count > 0)
            {
                await PublishSafelyAsync(new CardMentionEvent(
                    card.Id, card.Column.BoardId, userId, mentionedUserIds));
            }
        }

        card = (await LoadCardAsync(card.Id))!;
        return this.Protocol(new CardDetailsResponse
        {
            Code = changedFields.Count > 0 || oldAssigneeId != assignedUserId
                ? Code.JobDone
                : Code.NoActionTaken,
            Message = "Card updated.",
            Card = await ToDetailsDtoAsync(card, userId)
        });
    }

    [HttpDelete("{cardId:int}")]
    public async Task<IActionResult> Delete(int cardId)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var title = card.Title;
        var boardId = card.Column.BoardId;
        db.KanbanCardLabels.RemoveRange(db.KanbanCardLabels.Where(item => item.CardId == cardId));
        db.KanbanCardComments.RemoveRange(db.KanbanCardComments.Where(item => item.CardId == cardId));
        db.KanbanCardSubscriptions.RemoveRange(db.KanbanCardSubscriptions.Where(item => item.CardId == cardId));
        db.KanbanCards.Remove(card);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new CardDeletedEvent(cardId, title, boardId, userId));

        return this.Protocol(new AiurResponse
        {
            Code = Code.JobDone,
            Message = "Card deleted."
        });
    }

    [HttpGet("{cardId:int}/transfer-targets")]
    public async Task<IActionResult> TransferTargets(int cardId)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The source board is read-only.");
        }

        var userRoleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        var boards = await db.KanbanBoards
            .Where(board => !board.IsArchived && board.Id != card.Column.BoardId &&
                (board.UserId == userId || board.BoardShares.Any(share =>
                    share.Permission == SharePermission.Editable &&
                    (share.SharedWithUserId == userId ||
                     (share.SharedWithRoleId != null && userRoleIds.Contains(share.SharedWithRoleId))))))
            .Include(board => board.Columns)
            .OrderBy(board => board.Name)
            .ToListAsync();

        return this.Protocol(new CardTransferTargetsResponse
        {
            Code = Code.ResultShown,
            Message = "Editable transfer targets.",
            Boards = boards.Select(board => new CardTransferBoardDto
            {
                Id = board.Id,
                Name = board.Name,
                Columns = board.Columns
                    .OrderBy(column => column.Order)
                    .Select(column => new CardColumnOptionDto
                    {
                        Id = column.Id,
                        Name = column.Name
                    })
                    .ToList()
            }).ToList()
        });
    }

    [HttpPost("{cardId:int}/transfer")]
    public async Task<IActionResult> Transfer(int cardId, [FromBody] TransferCardRequest request)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.CardLabels)
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        var targetColumn = await db.KanbanColumns
            .Include(column => column.Board)
            .FirstOrDefaultAsync(column =>
                column.Id == request.TargetColumnId && column.BoardId == request.TargetBoardId);
        if (targetColumn == null)
        {
            return this.Protocol(Code.NotFound, "Target column not found.");
        }
        if (request.TargetBoardId == card.Column.BoardId)
        {
            return this.Protocol(Code.InvalidInput, "Target board must be different from the source board.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId) ||
            !await access.CanEditAsync(targetColumn.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "Both boards must be editable.");
        }

        var maxOrder = await db.KanbanCards
            .Where(item => item.ColumnId == targetColumn.Id)
            .MaxAsync(item => (int?)item.Order) ?? -1;
        var comments = await db.KanbanCardComments
            .Where(comment => comment.CardId == cardId)
            .ToListAsync();
        var sourceSubscriptions = await db.KanbanCardSubscriptions
            .Where(subscription => subscription.CardId == cardId)
            .ToListAsync();
        var transferredCard = new KanbanCard
        {
            Title = card.Title,
            Description = card.Description,
            Order = maxOrder + 1,
            ColumnId = targetColumn.Id,
            Priority = card.Priority,
            CreatorUserId = card.CreatorUserId ?? userId,
            AssignedUserId = null,
            PlannedStartTime = card.PlannedStartTime,
            DueDate = card.DueDate,
            RecurrenceInterval = card.RecurrenceInterval,
            RecurrenceUnit = card.RecurrenceUnit
        };
        db.KanbanCards.Add(transferredCard);
        transferredCard.Subscriptions.Add(new KanbanCardSubscription
        {
            Card = transferredCard,
            UserId = userId
        });
        db.KanbanCardLabels.AddRange(card.CardLabels.Select(link => new KanbanCardLabel
        {
            Card = transferredCard,
            LabelId = link.LabelId
        }));
        db.KanbanCardComments.RemoveRange(comments);
        db.KanbanCardSubscriptions.RemoveRange(sourceSubscriptions);
        db.KanbanCards.Remove(card);
        await db.SaveChangesAsync();

        await PublishSafelyAsync(new CardTransferredEvent(
            transferredCard.Id,
            userId,
            request.TargetBoardId,
            card.Id,
            card.Column.Board.Name,
            card.Column.Name));

        return this.Protocol(new CardTransferResponse
        {
            Code = Code.JobDone,
            Message = "Card transferred.",
            CardId = transferredCard.Id,
            BoardId = request.TargetBoardId,
            ColumnId = targetColumn.Id
        });
    }

    [HttpPost("{cardId:int}/comments")]
    public async Task<IActionResult> AddComment(int cardId, [FromBody] AddCardCommentRequest request)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var comment = new KanbanCardComment
        {
            CardId = cardId,
            AuthorId = userId,
            Content = request.Content.Trim(),
            Images = request.Images?.Trim() ?? string.Empty
        };
        db.KanbanCardComments.Add(comment);
        await CardSubscriptionService.SubscribeAsync(db, cardId, new[] { userId });
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new CardCommentAddedEvent(cardId, comment.Id, userId));

        var mentionedUserIds = await MentionParser.ExtractMentionedUserIds(
            db, comment.Content, card.Column.BoardId, HttpContext.RequestAborted);
        if (mentionedUserIds.Count > 0)
        {
            await PublishSafelyAsync(new CardMentionEvent(
                cardId, card.Column.BoardId, userId, mentionedUserIds));
        }

        var author = await userManager.FindByIdAsync(userId);
        return this.Protocol(new CardCommentResponse
        {
            Code = Code.JobDone,
            Message = "Comment added.",
            Comment = ToCommentDto(comment, author, true)
        });
    }

    [HttpPost("{cardId:int}/labels")]
    public async Task<IActionResult> AddLabel(int cardId, [FromBody] AddCardLabelRequest request)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var name = request.Name.Trim();
        var upperName = name.ToUpperInvariant();
        var label = await db.KanbanLabels
            .FirstOrDefaultAsync(item => item.Name.ToUpper() == upperName);
        var alreadyLinked = label != null && await db.KanbanCardLabels
            .AnyAsync(item => item.CardId == cardId && item.LabelId == label.Id);
        if (label == null)
        {
            label = new KanbanLabel
            {
                Name = name,
                Color = LabelColors[Random.Shared.Next(LabelColors.Length)]
            };
            db.KanbanLabels.Add(label);
        }
        if (!alreadyLinked)
        {
            db.KanbanCardLabels.Add(new KanbanCardLabel
            {
                CardId = cardId,
                Label = label
            });
            await db.SaveChangesAsync();
        }

        return this.Protocol(new CardLabelResponse
        {
            Code = alreadyLinked ? Code.NoActionTaken : Code.JobDone,
            Message = alreadyLinked ? "Label is already attached." : "Label added.",
            Label = new CardLabelDto
            {
                Id = label.Id,
                Name = label.Name,
                Color = label.Color
            }
        });
    }

    [HttpDelete("{cardId:int}/labels/{labelId:int}")]
    public async Task<IActionResult> RemoveLabel(int cardId, int labelId)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null)
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }
        if (!await access.CanEditAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }

        var link = await db.KanbanCardLabels
            .FirstOrDefaultAsync(item => item.CardId == cardId && item.LabelId == labelId);
        if (link == null)
        {
            return this.Protocol(Code.NotFound, "Card label not found.");
        }

        db.KanbanCardLabels.Remove(link);
        await db.SaveChangesAsync();
        return this.Protocol(new AiurResponse
        {
            Code = Code.JobDone,
            Message = "Label removed."
        });
    }

    [HttpDelete("{cardId:int}/comments/{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int cardId, int commentId)
    {
        var userId = CurrentUserId();
        var comment = await db.KanbanCardComments
            .Include(item => item.Card)
                .ThenInclude(card => card.Column)
                    .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == commentId && item.CardId == cardId);
        if (comment == null)
        {
            return this.Protocol(Code.NotFound, "Comment not found.");
        }

        var board = comment.Card.Column.Board;
        if (!await access.CanEditAsync(board, userId))
        {
            return this.Protocol(Code.Unauthorized, "The board is read-only.");
        }
        if (comment.AuthorId != userId && board.UserId != userId)
        {
            return this.Protocol(Code.Unauthorized, "Only the author or board owner can delete this comment.");
        }

        var cardTitle = comment.Card.Title;
        var boardName = board.Name;
        db.KanbanCardComments.Remove(comment);
        await db.SaveChangesAsync();
        await PublishSafelyAsync(new CardCommentDeletedEvent(
            cardId, commentId, userId, cardTitle, boardName));

        return this.Protocol(new AiurResponse
        {
            Code = Code.JobDone,
            Message = "Comment deleted."
        });
    }

    [HttpPut("{cardId:int}/subscription")]
    public async Task<IActionResult> SetSubscription(
        int cardId,
        [FromBody] SetCardSubscriptionRequest request)
    {
        var userId = CurrentUserId();
        var card = await db.KanbanCards
            .Include(item => item.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(item => item.Id == cardId);
        if (card == null || !await access.CanReadAsync(card.Column.Board, userId))
        {
            return this.Protocol(Code.NotFound, "Card not found.");
        }

        var existing = await db.KanbanCardSubscriptions
            .FirstOrDefaultAsync(item => item.CardId == cardId && item.UserId == userId);
        var changed = false;
        if (request.Subscribe && existing == null)
        {
            db.KanbanCardSubscriptions.Add(new KanbanCardSubscription
            {
                CardId = cardId,
                UserId = userId
            });
            changed = true;
        }
        else if (!request.Subscribe && existing != null)
        {
            db.KanbanCardSubscriptions.Remove(existing);
            changed = true;
        }
        if (changed)
        {
            await db.SaveChangesAsync();
        }

        return this.Protocol(new CardSubscriptionResponse
        {
            Code = changed ? Code.JobDone : Code.NoActionTaken,
            Message = request.Subscribe ? "Subscribed to card." : "Unsubscribed from card.",
            IsSubscribed = request.Subscribe
        });
    }

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private Task<KanbanCard?> LoadCardAsync(int cardId) => db.KanbanCards
        .Include(card => card.Column)
            .ThenInclude(column => column.Board)
        .Include(card => card.AssignedUser)
        .Include(card => card.CreatorUser)
        .Include(card => card.CardLabels)
            .ThenInclude(link => link.Label)
        .FirstOrDefaultAsync(card => card.Id == cardId);

    private async Task<CardDetailsDto> ToDetailsDtoAsync(KanbanCard card, string userId)
    {
        var canEdit = await access.CanEditAsync(card.Column.Board, userId);
        var comments = await db.KanbanCardComments
            .Where(comment => comment.CardId == card.Id)
            .Include(comment => comment.Author)
            .OrderBy(comment => comment.CreationTime)
            .ToListAsync();

        var assignees = new List<CardUserDto>();
        var columns = new List<CardColumnOptionDto>();
        var availableLabels = new List<CardLabelDto>();
        if (canEdit)
        {
            var accessibleUserIds = await access.GetAccessibleUserIdsAsync(card.Column.Board);
            var accessibleUsers = await db.Users
                .Where(user => accessibleUserIds.Contains(user.Id))
                .OrderBy(user => user.DisplayName)
                .ThenBy(user => user.UserName)
                .Select(user => new
                {
                    user.Id,
                    user.DisplayName,
                    user.UserName,
                    user.Email
                })
                .ToListAsync();
            assignees = accessibleUsers
                .Select(user => new CardUserDto
                {
                    Id = user.Id,
                    DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                        ? user.UserName ?? user.Email ?? user.Id
                        : user.DisplayName
                })
                .ToList();
            columns = await db.KanbanColumns
                .Where(column => column.BoardId == card.Column.BoardId)
                .OrderBy(column => column.Order)
                .Select(column => new CardColumnOptionDto
                {
                    Id = column.Id,
                    Name = column.Name
                })
                .ToListAsync();
            availableLabels = await db.KanbanLabels
                .OrderByDescending(label => label.CardLabels.Count)
                .ThenBy(label => label.Name)
                .Take(20)
                .Select(label => new CardLabelDto
                {
                    Id = label.Id,
                    Name = label.Name,
                    Color = label.Color
                })
                .ToListAsync();
        }

        return new CardDetailsDto
        {
            Id = card.Id,
            BoardId = card.Column.BoardId,
            BoardName = card.Column.Board.Name,
            ColumnId = card.ColumnId,
            ColumnName = card.Column.Name,
            Title = card.Title,
            Description = card.Description,
            Priority = card.Priority.ToString(),
            PlannedStartTime = card.PlannedStartTime,
            DueDate = card.DueDate,
            ActualStartTime = card.ActualStartTime,
            ActualEndTime = card.ActualEndTime,
            RecurrenceInterval = card.RecurrenceInterval,
            RecurrenceUnit = card.RecurrenceUnit.ToString(),
            CreationTime = card.CreationTime,
            CanEdit = canEdit,
            CanDelete = canEdit,
            IsSubscribed = await db.KanbanCardSubscriptions
                .AnyAsync(subscription => subscription.CardId == card.Id && subscription.UserId == userId),
            AssignedUser = ToUserDto(card.AssignedUser),
            CreatorUser = ToUserDto(card.CreatorUser),
            AvailableAssignees = assignees,
            AvailableColumns = columns,
            Labels = card.CardLabels
                .OrderBy(link => link.Label.Name)
                .Select(link => new CardLabelDto
                {
                    Id = link.LabelId,
                    Name = link.Label.Name,
                    Color = link.Label.Color
                })
                .ToList(),
            AvailableLabels = availableLabels,
            Comments = comments
                .Select(comment => ToCommentDto(
                    comment,
                    comment.Author,
                    canEdit && (comment.AuthorId == userId || card.Column.Board.UserId == userId)))
                .ToList()
        };
    }

    private static CardUserDto? ToUserDto(User? user) => user == null
        ? null
        : new CardUserDto
        {
            Id = user.Id,
            DisplayName = DisplayName(user)
        };

    private static CardCommentDto ToCommentDto(KanbanCardComment comment, User? author, bool canDelete) => new()
    {
        Id = comment.Id,
        Content = comment.Content,
        Images = comment.Images,
        Author = author == null
            ? new CardUserDto { Id = comment.AuthorId, DisplayName = "Unknown" }
            : new CardUserDto { Id = author.Id, DisplayName = DisplayName(author) },
        CreationTime = comment.CreationTime,
        CanDelete = canDelete
    };

    private static string DisplayName(User user) => string.IsNullOrWhiteSpace(user.DisplayName)
        ? user.UserName ?? user.Email ?? user.Id
        : user.DisplayName;

    private static string? NormalizeUserId(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();

    private static DateTime? NormalizeDateTime(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }
        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static void AddChangedField(List<string> fields, string field, bool changed)
    {
        if (changed)
        {
            fields.Add(field);
        }
    }

    private async Task PublishSafelyAsync<TNotification>(TNotification notification)
        where TNotification : INotification
    {
        try
        {
            await mediator.Publish(notification, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Failed to publish mobile API notification event {NotificationEvent}",
                typeof(TNotification).Name);
        }
    }
}
