using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardCommentAddedEvent(
    int CardId,
    int CommentId,
    string ActorUserId
) : INotification;
