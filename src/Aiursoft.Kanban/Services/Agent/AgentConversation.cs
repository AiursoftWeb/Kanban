namespace Aiursoft.Kanban.Services.Agent;

public class AgentConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = string.Empty;
    public int BoardId { get; init; }
    public AgentState State { get; set; } = AgentState.Thinking;
    public List<ToolMessagesItem> Messages { get; set; } = [];
    public List<Guid> PendingAdviceIds { get; set; } = [];
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public int LoopCount { get; set; }
}

public enum AgentState
{
    Thinking,
    AwaitingApproval,
    Completed,
    Error
}
