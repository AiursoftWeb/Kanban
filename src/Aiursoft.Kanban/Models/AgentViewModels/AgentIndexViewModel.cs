using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.AgentViewModels;

public class AgentIndexViewModel : UiStackLayoutViewModel
{
    public AgentIndexViewModel()
    {
        PageTitle = "Kanban AI Assistant";
    }

    public KanbanBoard? CurrentBoard { get; set; }
    public List<KanbanBoard> UserBoards { get; set; } = [];
}
