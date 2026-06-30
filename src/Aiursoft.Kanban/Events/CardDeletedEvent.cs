using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardDeletedEvent(
    int CardId,
    string CardTitle,
    int BoardId,
    string ActorUserId
) : INotification;
