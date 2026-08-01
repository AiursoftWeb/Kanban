using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications;

public static class CardSubscriptionService
{
    public static async Task SubscribeAsync(
        TemplateDbContext db, int cardId, IEnumerable<string?> userIds, CancellationToken ct = default)
    {
        var requestedIds = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        if (requestedIds.Count == 0) return;

        var existingIds = await db.KanbanCardSubscriptions
            .Where(s => s.CardId == cardId && requestedIds.Contains(s.UserId))
            .Select(s => s.UserId)
            .ToListAsync(ct);

        db.KanbanCardSubscriptions.AddRange(requestedIds
            .Except(existingIds)
            .Select(userId => new KanbanCardSubscription { CardId = cardId, UserId = userId }));
    }

    public static async Task<HashSet<string>> GetNotificationRecipientsAsync(
        TemplateDbContext db, int cardId, int boardId, string actorUserId, CancellationToken ct)
    {
        var subscriberIds = await db.KanbanCardSubscriptions
            .Where(s => s.CardId == cardId && s.UserId != actorUserId)
            .Select(s => s.UserId)
            .ToListAsync(ct);
        return await NotificationRecipientFilter.KeepUsersWithBoardReadAccess(db, boardId, subscriberIds, ct);
    }

    public static async Task RemoveSubscriptionsWithoutBoardAccessAsync(
        TemplateDbContext db, int boardId, CancellationToken ct = default)
    {
        var subscriptions = await db.KanbanCardSubscriptions
            .Where(subscription => subscription.Card.Column.BoardId == boardId)
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var allowedUserIds = await NotificationRecipientFilter.KeepUsersWithBoardReadAccess(
            db, boardId, subscriptions.Select(subscription => subscription.UserId), ct);
        db.KanbanCardSubscriptions.RemoveRange(
            subscriptions.Where(subscription => !allowedUserIds.Contains(subscription.UserId)));
    }
}
