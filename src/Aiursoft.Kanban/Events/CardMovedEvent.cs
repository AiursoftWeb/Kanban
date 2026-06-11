using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardMovedEvent(
    int CardId,
    string ActorUserId
) : INotification;
