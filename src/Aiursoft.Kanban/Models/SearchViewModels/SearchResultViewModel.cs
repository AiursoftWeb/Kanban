using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.SearchViewModels;

public class SearchResultViewModel : UiStackLayoutViewModel
{
    public required string Query { get; set; }
    public bool UsedAi { get; set; }
    public int TotalCount { get; set; }
    public List<KanbanCard> Cards { get; set; } = [];
}
