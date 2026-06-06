using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.DashboardViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Dashboard";
    }

    public required int OwnedBoardCount { get; init; }
    public required int SharedBoardCount { get; init; }
    public required int AssignedTaskCount { get; init; }
    public required int OverdueTaskCount { get; init; }
    public required int InProgressTaskCount { get; init; }
    public required List<KanbanCard> AssignedTasks { get; init; }
    public required List<BoardSummaryViewModel> OwnedBoards { get; init; }
    public required List<BoardSummaryViewModel> SharedBoards { get; init; }
}

public class BoardSummaryViewModel
{
    public required int BoardId { get; init; }
    public required string Name { get; init; }
    public required int TotalCards { get; init; }
    public required int IncompleteCards { get; init; }
    public required int InProgressCards { get; init; }
    public required int CompletedCards { get; init; }
    public required int OverdueCards { get; init; }
    public SharePermission? Permission { get; init; }
}
