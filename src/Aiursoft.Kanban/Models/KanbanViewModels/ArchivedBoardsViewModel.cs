using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class ArchivedBoardsViewModel : UiStackLayoutViewModel
{
    public ArchivedBoardsViewModel(
        IEnumerable<KanbanBoard> myArchivedBoards,
        IEnumerable<BoardShare> sharedArchivedShares,
        Dictionary<string, string> roleNames,
        Dictionary<int, BoardSummary> boardSummaries)
    {
        MyArchivedBoards = myArchivedBoards;
        SharedArchivedShares = sharedArchivedShares;
        RoleNames = roleNames;
        BoardSummaries = boardSummaries;
        PageTitle = "Archived Boards";
    }

    public IEnumerable<KanbanBoard> MyArchivedBoards { get; init; }
    public IEnumerable<BoardShare> SharedArchivedShares { get; init; }
    public Dictionary<string, string> RoleNames { get; init; }
    public Dictionary<int, BoardSummary> BoardSummaries { get; init; }
}