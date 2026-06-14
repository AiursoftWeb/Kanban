using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class CardTransferredHandler(TemplateDbContext db) : INotificationHandler<CardTransferredEvent>
{
    public async Task Handle(CardTransferredEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var targetBoard = await db.KanbanBoards
            .FirstOrDefaultAsync(b => b.Id == e.TargetBoardId, ct);
        if (targetBoard == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);

        var notifyIds = new HashSet<string>();
        if (!string.IsNullOrEmpty(e.OriginalCreatorUserId))
            notifyIds.Add(e.OriginalCreatorUserId);
        if (!string.IsNullOrEmpty(e.OriginalAssigneeUserId))
            notifyIds.Add(e.OriginalAssigneeUserId);

        notifyIds.Remove(e.ActorUserId);

        foreach (var userId in notifyIds)
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = userId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CardTransferred,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CardTransferred,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title,
                        ["BoardName"] = targetBoard.Name
                    })
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
