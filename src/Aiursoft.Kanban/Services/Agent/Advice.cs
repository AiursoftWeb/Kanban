namespace Aiursoft.Kanban.Services.Agent;

public class Advice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ConversationId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string ToolDisplayName { get; init; } = string.Empty;
    public string ToolDescription { get; init; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; init; } = new();
    public string ParameterDisplay { get; init; } = string.Empty;
    public string? ToolCallId { get; init; }
    public AdviceStatus Status { get; set; } = AdviceStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public enum AdviceStatus
{
    Pending,
    Approved,
    Rejected,
    Executed,
    Failed
}
