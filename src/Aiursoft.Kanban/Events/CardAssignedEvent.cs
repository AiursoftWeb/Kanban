using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardAssignedEvent(
    int CardId,
    string ActorUserId,
    string? OldAssigneeId,
    string? NewAssigneeId
) : INotification;
