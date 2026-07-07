using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardCommentDeletedEvent(
    int CardId,
    int CommentId,
    string ActorUserId,
    string CardTitle,
    string BoardName
) : INotification;
