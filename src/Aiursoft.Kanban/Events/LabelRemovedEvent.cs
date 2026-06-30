using MediatR;

namespace Aiursoft.Kanban.Events;

public record LabelRemovedEvent(
    int CardId,
    int LabelId,
    string LabelName,
    int BoardId,
    string ActorUserId
) : INotification;
