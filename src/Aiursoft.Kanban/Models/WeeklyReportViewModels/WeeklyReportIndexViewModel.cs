using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.WeeklyReportViewModels;

public class WeeklyReportIndexViewModel : UiStackLayoutViewModel
{
    public WeeklyReportIndexViewModel()
    {
        PageTitle = "Weekly Report";
    }

    public required List<WeeklyReportItemViewModel> Reports { get; init; }
    public WeeklyReportItemViewModel? ThisWeekReport { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public bool CanGenerate { get; init; }
    public DateTime? CurrentWeekStart { get; init; }
}
