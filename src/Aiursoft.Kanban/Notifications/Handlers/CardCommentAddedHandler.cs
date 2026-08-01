using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications.Handlers;

public class CardCommentAddedHandler(TemplateDbContext db) : INotificationHandler<CardCommentAddedEvent>
{
    public async Task Handle(CardCommentAddedEvent e, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
            .FirstOrDefaultAsync(c => c.Id == e.CardId, ct);
        if (card == null) return;

        var actorName = await GetUserDisplayName(db, e.ActorUserId);

        var notifyIds = await CardSubscriptionService.GetNotificationRecipientsAsync(
            db, e.CardId, card.Column.BoardId, e.ActorUserId, ct);

        foreach (var userId in notifyIds)
        {
            db.Notifications.Add(new Notification
            {
                CardId = e.CardId,
                CommentId = e.CommentId,
                UserId = userId,
                ActorUserId = e.ActorUserId,
                Type = NotificationType.CommentAdded,
                Message = NotificationTemplateService.BuildMessage(NotificationType.CommentAdded,
                    new Dictionary<string, string>
                    {
                        ["ActorName"] = actorName,
                        ["CardTitle"] = card.Title
                    })
            });
        }

        await db.SaveChangesAsync(ct);
    }

    internal static async Task<string> GetUserDisplayName(TemplateDbContext db, string userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return "Unknown";
        return string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? userId
            : user.DisplayName;
    }
}
