using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Notifications;

public static class NotificationTemplateService
{
    public static string BuildMessage(NotificationType type, Dictionary<string, string> args) => type switch
    {
        NotificationType.CommentAdded => $"{args["ActorName"]} commented on card \"{args["CardTitle"]}\"",
        NotificationType.CardAssigned => $"{args["ActorName"]} assigned you to card \"{args["CardTitle"]}\"",
        NotificationType.CardUnassigned => $"{args["ActorName"]} removed you from card \"{args["CardTitle"]}\"",
        NotificationType.CardMoved => $"{args["ActorName"]} moved card \"{args["CardTitle"]}\" to {args["ColumnName"]}",
        NotificationType.CardTransferred => $"{args["ActorName"]} transferred card \"{args["CardTitle"]}\" to board \"{args["BoardName"]}\"",
        NotificationType.CardUpdated => $"{args["ActorName"]} updated {args["ChangedFields"]} on card \"{args["CardTitle"]}\"",
        NotificationType.BoardShared => $"{args["ActorName"]} shared board \"{args["BoardName"]}\" with you",
        NotificationType.Mentioned => $"{args["ActorName"]} mentioned you in card \"{args["CardTitle"]}\"",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string BuildMessage(Notification notification)
    {
        if (!string.IsNullOrWhiteSpace(notification.Message))
            return notification.Message;

        var actorName = GetUserDisplayName(notification.ActorUser)
            ?? GetUserDisplayName(notification.Comment?.Author)
            ?? "Someone";

        return notification.Type switch
        {
            NotificationType.CommentAdded when notification.Card != null => BuildMessage(NotificationType.CommentAdded,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["CardTitle"] = notification.Card.Title
                }),
            NotificationType.CardAssigned when notification.Card != null => BuildMessage(NotificationType.CardAssigned,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["CardTitle"] = notification.Card.Title
                }),
            NotificationType.CardUnassigned when notification.Card != null => BuildMessage(NotificationType.CardUnassigned,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["CardTitle"] = notification.Card.Title
                }),
            NotificationType.CardMoved when notification.Card != null => BuildMessage(NotificationType.CardMoved,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["CardTitle"] = notification.Card.Title,
                    ["ColumnName"] = notification.Card.Column.Name
                }),
            NotificationType.CardTransferred when notification.Card != null => $"{actorName} transferred card \"{notification.Card.Title}\"",
            NotificationType.CardUpdated when notification.Card != null => $"{actorName} updated card \"{notification.Card.Title}\"",
            NotificationType.BoardShared => $"{actorName} shared a board with you",
            NotificationType.Mentioned when notification.Card != null => BuildMessage(NotificationType.Mentioned,
                new Dictionary<string, string>
                {
                    ["ActorName"] = actorName,
                    ["CardTitle"] = notification.Card.Title
                }),
            _ => "You have a new notification"
        };
    }

    private static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }
}
