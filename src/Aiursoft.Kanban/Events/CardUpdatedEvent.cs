using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardUpdatedEvent(
    int CardId,
    string ActorUserId,
    IReadOnlyList<string> ChangedFields
) : INotification;
