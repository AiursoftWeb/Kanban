namespace Aiursoft.Kanban.Models.AgentViewModels;

public class SendMessageRequest
{
    public int BoardId { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? ConversationId { get; set; }
    public string? ExcelMarkdown { get; set; }
}
