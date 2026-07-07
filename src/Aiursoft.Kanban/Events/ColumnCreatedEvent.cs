using MediatR;

namespace Aiursoft.Kanban.Events;

public record ColumnCreatedEvent(
    int ColumnId,
    string ColumnName,
    int BoardId,
    string ActorUserId
) : INotification;
