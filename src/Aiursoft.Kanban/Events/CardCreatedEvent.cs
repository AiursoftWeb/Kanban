using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardCreatedEvent(
    int CardId,
    string CardTitle,
    int ColumnId,
    int BoardId,
    string ActorUserId
) : INotification;
