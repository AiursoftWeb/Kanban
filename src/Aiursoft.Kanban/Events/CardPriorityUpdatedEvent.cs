using Aiursoft.Kanban.Entities;
using MediatR;

namespace Aiursoft.Kanban.Events;

public record CardPriorityUpdatedEvent(
    int CardId,
    string ActorUserId,
    Priority OldPriority,
    Priority NewPriority
) : INotification;
