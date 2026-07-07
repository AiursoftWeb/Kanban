using MediatR;

namespace Aiursoft.Kanban.Events;

public record ColumnMovedEvent(
    int ColumnId,
    string ColumnName,
    int BoardId,
    int OldOrder,
    int NewOrder,
    string ActorUserId
) : INotification;
