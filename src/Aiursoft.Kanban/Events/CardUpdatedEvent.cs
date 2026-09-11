using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardUpdatedEvent(
    int CardId,
    string ActorUserId,
    IReadOnlyList<string> ChangedFields,
    CardActualTimeChange? ActualTimeChange = null
) : INotification;

public record CardActualTimeChange(
    DateTime? OldStartTime,
    DateTime? OldEndTime,
    DateTime? NewStartTime,
    DateTime? NewEndTime);
