using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Kanban Board";
    }

    public List<KanbanBoard> Boards { get; set; } = [];
    public Dictionary<int, BoardSummary> BoardSummaries { get; set; } = [];
    public KanbanBoard? CurrentBoard { get; set; }
    public bool IsOwner { get; set; } = true;
    public bool CanEditCurrentBoard { get; set; } = true;

    /// <summary>JSON-serializable board data for the TypeScript module.</summary>
    public BoardData? BoardData { get; set; }
}
