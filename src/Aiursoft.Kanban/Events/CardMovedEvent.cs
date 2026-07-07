using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardMovedEvent(
    int CardId,
    string ActorUserId,
    int FromColumnId,
    string FromColumnName,
    int ToColumnId,
    string ToColumnName,
    int NewOrder,
    bool NotifyUsers = true
) : INotification;
