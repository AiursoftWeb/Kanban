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
        NotificationType.CardUpdated => $"{args["ActorName"]} updated {string.Join(", ", args["ChangedFields"].Split(','))} on card \"{args["CardTitle"]}\"",
        NotificationType.BoardShared => $"{args["ActorName"]} shared board \"{args["BoardName"]}\" with you",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
