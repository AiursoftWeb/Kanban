namespace Aiursoft.Kanban.Models.AgentViewModels;

public class AdviceViewModel
{
    public Guid AdviceId { get; set; }
    public string ToolDisplayName { get; set; } = string.Empty;
    public string ParameterDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}
