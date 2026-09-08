using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services;

public sealed class CardCopyService(
    TemplateDbContext db,
    KanbanApiAccessService access) : IScopedDependency
{
    public async Task<CardCopyResult> CopyAsync(int cardId, string userId)
    {
        var sourceCard = await db.KanbanCards
            .Include(card => card.CardLabels)
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(card => card.Id == cardId);
        if (sourceCard == null)
        {
            return CardCopyResult.NotFound();
        }
        if (!await access.CanEditAsync(sourceCard.Column.Board, userId))
        {
            return CardCopyResult.Forbidden();
        }

        var maxOrder = await db.KanbanCards
            .Where(card => card.ColumnId == sourceCard.ColumnId)
            .MaxAsync(card => (int?)card.Order) ?? -1;
        const string copiedSuffix = " Copied";
        const int titleMaxLength = 200;
        var sourceTitle = sourceCard.Title.Length > titleMaxLength - copiedSuffix.Length
            ? sourceCard.Title[..(titleMaxLength - copiedSuffix.Length)]
            : sourceCard.Title;
        var now = DateTime.UtcNow;
        var copiedCard = new KanbanCard
        {
            Title = sourceTitle + copiedSuffix,
            Description = sourceCard.Description,
            Order = maxOrder + 1,
            ColumnId = sourceCard.ColumnId,
            Priority = sourceCard.Priority,
            AssignedUserId = sourceCard.AssignedUserId,
            CreatorUserId = userId,
            PlannedStartTime = sourceCard.PlannedStartTime,
            DueDate = sourceCard.DueDate,
            ActualStartTime = sourceCard.ActualStartTime,
            ActualEndTime = sourceCard.ActualEndTime,
            RecurrenceInterval = sourceCard.RecurrenceInterval,
            RecurrenceUnit = sourceCard.RecurrenceUnit,
            CreationTime = now,
            LastUpdatedAt = now
        };

        db.KanbanCards.Add(copiedCard);
        copiedCard.Subscriptions.Add(new KanbanCardSubscription
        {
            Card = copiedCard,
            UserId = userId
        });
        db.KanbanCardLabels.AddRange(sourceCard.CardLabels.Select(link => new KanbanCardLabel
        {
            Card = copiedCard,
            LabelId = link.LabelId
        }));
        await db.SaveChangesAsync();

        return CardCopyResult.Success(
            copiedCard.Id,
            sourceCard.Column.BoardId,
            copiedCard.ColumnId);
    }
}

public enum CardCopyStatus
{
    Success,
    NotFound,
    Forbidden
}

public sealed record CardCopyResult(
    CardCopyStatus Status,
    int CardId = 0,
    int BoardId = 0,
    int ColumnId = 0)
{
    public static CardCopyResult Success(int cardId, int boardId, int columnId) =>
        new(CardCopyStatus.Success, cardId, boardId, columnId);

    public static CardCopyResult NotFound() => new(CardCopyStatus.NotFound);

    public static CardCopyResult Forbidden() => new(CardCopyStatus.Forbidden);
}
