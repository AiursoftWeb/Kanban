namespace Aiursoft.Kanban.Services.Agent;

public sealed record AgentExecutionOptions
{
    public bool AutoApproveWrites { get; init; }
    public string? SystemPromptOverride { get; init; }
}

public sealed record AgentToolTrace
{
    public AgentToolTrace(
        string toolCallId,
        string name,
        Dictionary<string, object?> parameters,
        string result,
        int loop)
    {
        ToolCallId = toolCallId;
        Name = name;
        Parameters = parameters;
        Result = result;
        Loop = loop;
    }

    public string ToolCallId { get; }
    public string Name { get; }
    public Dictionary<string, object?> Parameters { get; }
    public string Result { get; }
    public int Loop { get; }
}

public sealed record AgentExecutionResult(
    AgentConversation Conversation,
    IReadOnlyList<AgentToolTrace> ToolTraces);
