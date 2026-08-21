namespace Aiursoft.Kanban.Services.Agent;

public sealed record AgentExecutionOptions
{
    public bool AutoApproveWrites { get; init; }
}

public sealed record AgentToolTrace(
    string ToolCallId,
    string Name,
    Dictionary<string, object?> Parameters,
    string Result,
    int Loop);

public sealed record AgentExecutionResult(
    AgentConversation Conversation,
    IReadOnlyList<AgentToolTrace> ToolTraces);
