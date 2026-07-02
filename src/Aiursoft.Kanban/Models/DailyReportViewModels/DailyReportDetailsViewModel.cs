using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.DailyReportViewModels;

public class DailyReportDetailsViewModel : UiStackLayoutViewModel
{
    public DailyReportDetailsViewModel()
    {
        PageTitle = "Report Details";
    }

    public required DailyReport Report { get; init; }
}
