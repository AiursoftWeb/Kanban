using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardMovedEvent(
    int CardId,
    string ActorUserId,
    int FromColumnId,
    int ToColumnId
) : INotification;
