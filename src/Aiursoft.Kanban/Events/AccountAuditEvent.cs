using MediatR;

namespace Aiursoft.Kanban.Events;

public record AccountAuditEvent(
    string Action,
    string Summary,
    string? UserId,
    string? UserName,
    string Source = "Web"
) : INotification;
