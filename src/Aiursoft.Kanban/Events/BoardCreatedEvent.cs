using MediatR;

namespace Aiursoft.Kanban.Events;

public record BoardCreatedEvent(
    int BoardId,
    string BoardName,
    string ActorUserId
) : INotification;
