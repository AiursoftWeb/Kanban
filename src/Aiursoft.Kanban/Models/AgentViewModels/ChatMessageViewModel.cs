namespace Aiursoft.Kanban.Models.AgentViewModels;

public class ChatMessageViewModel
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public List<ToolCallViewModel>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public Guid? AdviceId { get; set; }
    public string? AdviceStatus { get; set; }
}

public class ToolCallViewModel
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}
