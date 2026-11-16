using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class AllBoardsViewModel : UiStackLayoutViewModel
{
    public AllBoardsViewModel()
    {
        PageTitle = "All Boards";
    }

    public required List<KanbanBoard> Boards { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int Total { get; init; }
}
