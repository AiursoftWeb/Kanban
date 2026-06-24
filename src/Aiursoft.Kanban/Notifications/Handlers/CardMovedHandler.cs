using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class CardMovedHandler(TemplateDbContext db) : INotificationHandler<CardMovedEvent>
{
    public async Task Handle(CardMovedEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);

        var notifyIds = new HashSet<string>();
        if (!string.IsNullOrEmpty(card.CreatorUserId))
            notifyIds.Add(card.CreatorUserId);
        if (!string.IsNullOrEmpty(card.AssignedUserId))
            notifyIds.Add(card.AssignedUserId);

        notifyIds.Remove(e.ActorUserId);
        notifyIds = await NotificationRecipientFilter.KeepUsersWithBoardReadAccess(db, card.Column.BoardId, notifyIds, ct);

        foreach (var userId in notifyIds)
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = userId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CardMoved,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CardMoved,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title,
                        ["ColumnName"] = card.Column.Name
                    })
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
