using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.WeeklyReportViewModels;

public class WeeklyReportDetailsViewModel : UiStackLayoutViewModel
{
    public WeeklyReportDetailsViewModel()
    {
        PageTitle = "Weekly Report Details";
    }

    public required WeeklyReport Report { get; init; }
}
