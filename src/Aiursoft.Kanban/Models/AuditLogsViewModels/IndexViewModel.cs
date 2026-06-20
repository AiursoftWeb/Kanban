using Aiursoft.Kanban.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.AuditLogsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Operation Logs";
    }
    public IReadOnlyList<AuditLog> Logs { get; init; } = [];
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public bool ShowingAllUsers { get; init; }
    public bool Enabled { get; init; }
}
