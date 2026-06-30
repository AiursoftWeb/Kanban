using MediatR;

namespace Aiursoft.Kanban.Events;

public record LabelAddedEvent(
    int CardId,
    int LabelId,
    string LabelName,
    string LabelColor,
    int BoardId,
    string ActorUserId
) : INotification;
