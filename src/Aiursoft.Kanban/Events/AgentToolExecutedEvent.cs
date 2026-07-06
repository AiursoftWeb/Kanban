using MediatR;

namespace Aiursoft.Kanban.Events;

public record AgentToolExecutedEvent(
    string ToolName,
    string UserId,
    string UserName,
    string Summary,
    IReadOnlyDictionary<string, object?> Arguments
) : INotification;
