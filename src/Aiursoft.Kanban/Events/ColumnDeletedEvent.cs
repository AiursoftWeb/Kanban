using MediatR;

namespace Aiursoft.Kanban.Events;

public record ColumnDeletedEvent(
    int ColumnId,
    string ColumnName,
    int BoardId,
    string ActorUserId
) : INotification;
