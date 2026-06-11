using MediatR;

namespace Aiursoft.Kanban.Events;

public record BoardSharedEvent(
    int BoardId,
    string ActorUserId,
    string SharedWithUserId
) : INotification;
