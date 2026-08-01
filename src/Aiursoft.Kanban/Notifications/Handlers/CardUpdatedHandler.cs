using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class CardUpdatedHandler(TemplateDbContext db) : INotificationHandler<CardUpdatedEvent>
{
    public async Task Handle(CardUpdatedEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);

        var notifyIds = await CardSubscriptionService.GetNotificationRecipientsAsync(
            db, e.CardId, card.Column.BoardId, e.ActorUserId, ct);

        foreach (var userId in notifyIds)
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = userId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CardUpdated,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CardUpdated,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title,
                        ["ChangedFields"] = string.Join(", ", e.ChangedFields)
                    })
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
