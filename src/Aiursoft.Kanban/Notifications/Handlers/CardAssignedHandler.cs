using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class CardAssignedHandler(TemplateDbContext db) : INotificationHandler<CardAssignedEvent>
{
    public async Task Handle(CardAssignedEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var actorName = await CardCommentAddedHandler.GetUserDisplayName(db, e.ActorUserId);
        var allowedIds = await NotificationRecipientFilter.KeepUsersWithBoardReadAccess(
            db,
            card.Column.BoardId,
            new[] { e.NewAssigneeId, e.OldAssigneeId }.OfType<string>(),
            ct);

        // Notify new assignee
        if (!string.IsNullOrEmpty(e.NewAssigneeId) && e.NewAssigneeId != e.ActorUserId && allowedIds.Contains(e.NewAssigneeId))
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = e.NewAssigneeId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CardAssigned,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CardAssigned,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title
                    })
            });
        }

        // Notify old assignee about removal
        if (!string.IsNullOrEmpty(e.OldAssigneeId)
            && e.OldAssigneeId != e.NewAssigneeId
            && e.OldAssigneeId != e.ActorUserId
            && allowedIds.Contains(e.OldAssigneeId))
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                UserId = e.OldAssigneeId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CardUnassigned,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CardUnassigned,
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
