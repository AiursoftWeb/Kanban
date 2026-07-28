namespace Aiursoft.Kanban.Models.WeeklyReportViewModels;

public class WeeklyReportItemViewModel
{
    public Guid Id { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime WeekStart { get; init; }
    public DateTime GeneratedAt { get; init; }
}
