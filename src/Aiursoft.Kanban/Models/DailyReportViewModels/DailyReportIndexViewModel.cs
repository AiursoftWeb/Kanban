using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.DailyReportViewModels;

public class DailyReportIndexViewModel : UiStackLayoutViewModel
{
    public DailyReportIndexViewModel()
    {
        PageTitle = "Daily Assistant";
    }

    public required List<DailyReportItemViewModel> Reports { get; init; }
    public DailyReportItemViewModel? TodayPlan { get; init; }
    public DailyReportItemViewModel? TodaySummary { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public bool CanPlan { get; init; }
    public bool CanSummarize { get; init; }
}
