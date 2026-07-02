using MediatR;

namespace Aiursoft.Kanban.Events;

public record RecurringCardResetEvent(
    int CardId,
    string ActorUserId,
    int FromColumnId,
    string FromColumnName,
    int ToColumnId,
    string ToColumnName,
    int NewOrder
) : INotification;
