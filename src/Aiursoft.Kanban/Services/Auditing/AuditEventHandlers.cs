using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditEventHandlers(
    TemplateDbContext db,
    AuditLogService auditLogService) :
    INotificationHandler<AccountAuditEvent>,
    INotificationHandler<AgentToolExecutedEvent>,
    INotificationHandler<BoardSharedEvent>,
    INotificationHandler<CardAssignedEvent>,
    INotificationHandler<CardCommentAddedEvent>,
    INotificationHandler<CardCommentDeletedEvent>,
    INotificationHandler<CardMovedEvent>,
    INotificationHandler<CardPriorityUpdatedEvent>,
    INotificationHandler<CardTransferredEvent>,
    INotificationHandler<CardUpdatedEvent>
{
    public Task Handle(AccountAuditEvent e, CancellationToken ct)
    {
        return auditLogService.RecordAsync(
            action: e.Action,
            category: "Account",
            summary: e.Summary,
            source: e.Source,
            userId: e.UserId,
            userName: e.UserName,
            cancellationToken: ct);
    }

    public Task Handle(AgentToolExecutedEvent e, CancellationToken ct)
    {
        return auditLogService.RecordAsync(
            action: $"Agent.{e.ToolName}",
            category: "Kanban",
            summary: e.Summary,
            details: AuditDetailFilter.ToSafeDictionary(e.Arguments),
            source: "Agent",
            userId: e.UserId,
            userName: e.UserName,
            cancellationToken: ct);
    }

    public async Task Handle(BoardSharedEvent e, CancellationToken ct)
    {
        var board = await db.KanbanBoards.FirstOrDefaultAsync(b => b.Id == e.BoardId, ct);
        if (board == null) return;

        await auditLogService.RecordAsync(
            "Kanban.ShareBoard",
            "Kanban",
            $"Shared board \"{board.Name}\"",
            new { e.BoardId, Board = board.Name, e.SharedWithUserId },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardAssignedEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.AssignCard",
            "Kanban",
            $"Changed assignee of card \"{card.Title}\"",
            new
            {
                CardId = card.Id,
                Board = card.Column.Board.Name,
                e.OldAssigneeId,
                e.NewAssigneeId
            },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardCommentAddedEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.AddComment",
            "Kanban",
            $"Commented on card \"{card.Title}\"",
            new { CardId = card.Id, e.CommentId, Board = card.Column.Board.Name },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public Task Handle(CardCommentDeletedEvent e, CancellationToken ct)
    {
        return auditLogService.RecordAsync(
            "Kanban.DeleteComment",
            "Kanban",
            $"Deleted a comment from card \"{e.CardTitle}\"",
            new { e.CardId, e.CommentId, Board = e.BoardName },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardMovedEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.MoveCard",
            "Kanban",
            $"Moved card \"{card.Title}\" from {e.FromColumnName} to {e.ToColumnName}",
            new
            {
                e.CardId,
                e.FromColumnId,
                e.FromColumnName,
                e.ToColumnId,
                e.ToColumnName,
                e.NewOrder,
                Board = card.Column.Board.Name
            },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardPriorityUpdatedEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.UpdateCardPriority",
            "Kanban",
            $"Changed priority of card \"{card.Title}\" from {e.OldPriority} to {e.NewPriority}",
            new
            {
                CardId = card.Id,
                Board = card.Column.Board.Name,
                OldPriority = e.OldPriority.ToString(),
                NewPriority = e.NewPriority.ToString()
            },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardTransferredEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.TransferCard",
            "Kanban",
            $"Transferred card \"{card.Title}\" from {e.SourceBoardName}/{e.SourceColumnName} to {card.Column.Board.Name}/{card.Column.Name}",
            new
            {
                e.OriginalCardId,
                NewCardId = card.Id,
                SourceBoard = e.SourceBoardName,
                SourceColumn = e.SourceColumnName,
                TargetBoard = card.Column.Board.Name,
                TargetColumn = card.Column.Name
            },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    public async Task Handle(CardUpdatedEvent e, CancellationToken ct)
    {
        var card = await LoadCardAsync(e.CardId, ct);
        if (card == null) return;

        await auditLogService.RecordAsync(
            "Kanban.UpdateCardDetails",
            "Kanban",
            $"Updated card \"{card.Title}\": {string.Join(", ", e.ChangedFields)}",
            new { CardId = card.Id, Board = card.Column.Board.Name, e.ChangedFields },
            userId: e.ActorUserId,
            cancellationToken: ct);
    }

    private Task<KanbanCard?> LoadCardAsync(int cardId, CancellationToken ct)
    {
        return db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct);
    }
}
