using MediatR;

namespace Aiursoft.Kanban.Events;

public record BoardRenamedEvent(
    int BoardId,
    string OldName,
    string NewName,
    string ActorUserId
) : INotification;
