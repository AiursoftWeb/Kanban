using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardMentionEvent(
    int CardId,
    int BoardId,
    string ActorUserId,
    HashSet<string> MentionedUserIds
) : INotification;
