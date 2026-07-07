using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardTransferredEvent(
    int CardId,
    string ActorUserId,
    int TargetBoardId,
    int OriginalCardId,
    string SourceBoardName,
    string SourceColumnName,
    string? OriginalCreatorUserId,
    string? OriginalAssigneeUserId
) : INotification;
