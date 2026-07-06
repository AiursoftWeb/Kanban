using MediatR;

namespace Aiursoft.Kanban.Events;

public record LabelColorUpdatedEvent(
    int CardId,
    int LabelId,
    string LabelName,
    string OldColor,
    string NewColor,
    int BoardId,
    string ActorUserId
) : INotification;
