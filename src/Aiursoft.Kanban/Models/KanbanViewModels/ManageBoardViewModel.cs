using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class ManageBoardViewModel : UiStackLayoutViewModel
{
    public ManageBoardViewModel()
    {
        PageTitle = "Edit Board";
    }

    public required int BoardId { get; init; }
    public required string BoardName { get; init; }
    public required int BoardOrder { get; init; }
    public required List<KanbanColumn> Columns { get; init; }
}
