using MediatR;

namespace Aiursoft.Kanban.Events;

public record BoardMovedEvent(
    int BoardId,
    string BoardName,
    int OldOrder,
    int NewOrder,
    string ActorUserId
) : INotification;
