namespace Aiursoft.Kanban.Models.AgentViewModels;

public class AdviceViewModel
{
    public Guid AdviceId { get; set; }
    public string ToolDisplayName { get; set; } = string.Empty;
    public string ParameterDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public List<ParameterItemViewModel> Parameters { get; set; } = [];
    public string? ResolvedName { get; set; }
}

public class ParameterItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string DisplayKey { get; set; } = string.Empty;
    public string? Value { get; set; }
}
