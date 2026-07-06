using MediatR;

namespace Aiursoft.Kanban.Events;

public record ColumnStatusUpdatedEvent(
    int ColumnId,
    string ColumnName,
    int OldStatus,
    int NewStatus,
    int BoardId,
    string ActorUserId
) : INotification;
