using MediatR;
using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Events;

public record BoardSharedEvent(
    int BoardId,
    string ActorUserId,
    string SharedWithUserId,
    SharePermission Permission
) : INotification;
