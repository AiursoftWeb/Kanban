using MediatR;

namespace Aiursoft.Kanban.Events;

public record BoardDeletedEvent(
    int BoardId,
    string BoardName,
    string ActorUserId
) : INotification;
