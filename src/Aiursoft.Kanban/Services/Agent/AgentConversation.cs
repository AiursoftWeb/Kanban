namespace Aiursoft.Kanban.Services.Agent;

public class AgentConversation
{
    private readonly CancellationTokenSource _cancellation = new();
    private int _cancelled;
    internal CancellationToken CancellationToken { get; }

    public AgentConversation() => CancellationToken = _cancellation.Token;

    internal void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) != 0) return;
        try
        {
            _cancellation.Cancel();
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

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
