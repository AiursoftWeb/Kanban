using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardTransferredEvent(
    int CardId,
    string ActorUserId,
    int TargetBoardId
) : INotification;
