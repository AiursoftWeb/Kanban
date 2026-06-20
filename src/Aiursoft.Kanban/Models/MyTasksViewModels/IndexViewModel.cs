using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.MyTasksViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "My Tasks";
    }

    public required List<KanbanCard> Cards { get; init; }
    public required List<LabelFilterViewModel> AvailableLabels { get; init; }
    public required HashSet<int> SelectedLabelIds { get; init; }
    public required string SelectedStatus { get; init; }
    public required string SelectedLabelMode { get; init; }
    public required string SelectedSort { get; init; }
}
