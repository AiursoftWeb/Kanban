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

    /// <summary>Display name of the board's creator. Populated when the current user is not the owner.</summary>
    public string? BoardCreatorDisplayName { get; set; }

    /// <summary>Avatar URL of the board's creator. Null if using default avatar.</summary>
    public string? BoardCreatorAvatarUrl { get; set; }

    /// <summary>First initial of the board creator's display name, for avatar fallback.</summary>
    public string? BoardCreatorInitial { get; set; }

    /// <summary>JSON-serializable board data for the TypeScript module.</summary>
    public BoardData? BoardData { get; set; }
}
