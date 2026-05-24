using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class SharedWithMeViewModel : UiStackLayoutViewModel
{
    public SharedWithMeViewModel()
    {
        PageTitle = "Shared with Me";
    }

    public required List<BoardShare> Shares { get; init; }
    public required Dictionary<string, string> RoleNames { get; init; }
}
