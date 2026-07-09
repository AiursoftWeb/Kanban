using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class GanttChartViewModel : UiStackLayoutViewModel
{
    public GanttChartViewModel()
    {
        PageTitle = "Gantt Chart";
    }

    public KanbanBoard Board { get; set; } = null!;

    /// <summary>JSON-serializable board data for the TypeScript Gantt module.</summary>
    public BoardData BoardData { get; set; } = null!;
}
