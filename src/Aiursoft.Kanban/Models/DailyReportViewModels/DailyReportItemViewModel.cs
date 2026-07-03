using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Models.DailyReportViewModels;

public class DailyReportItemViewModel
{
    public Guid Id { get; init; }
    public DailyReportType ReportType { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public DateTime GeneratedAt { get; init; }
}
