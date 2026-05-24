using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class PublicBoardViewModel : UiStackLayoutViewModel
{
    public PublicBoardViewModel(string pageTitle)
    {
        PageTitle = pageTitle;
    }

    public required KanbanBoard Board { get; init; }
    public bool CanEdit { get; init; }
}
