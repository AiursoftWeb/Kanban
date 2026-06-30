using MediatR;

namespace Aiursoft.Kanban.Events;

public record ColumnRenamedEvent(
    int ColumnId,
    string OldName,
    string NewName,
    int BoardId,
    string ActorUserId
) : INotification;
