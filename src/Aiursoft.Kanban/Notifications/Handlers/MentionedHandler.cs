using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class MentionedHandler(TemplateDbContext db) : INotificationHandler<CardMentionEvent>
{
    public async Task Handle(CardMentionEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);

        // Exclude the actor (can't @ yourself)
        e.MentionedUserIds.Remove(e.ActorUserId);

        // Filter: only users with board read access get notified
        var notifiableIds = await NotificationRecipientFilter.KeepUsersWithBoardReadAccess(
            db, e.BoardId, e.MentionedUserIds, ct);

        await CardSubscriptionService.SubscribeAsync(db, e.CardId, notifiableIds, ct);

        foreach (var userId in notifiableIds)
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = userId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.Mentioned,
                Message = NotificationTemplateService.BuildMessage(NotificationType.Mentioned,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title
                    })
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
