using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanCardLabel
{
    public int CardId { get; set; }
    public KanbanCard Card { get; set; } = null!;

    public int LabelId { get; set; }
    public KanbanLabel Label { get; set; } = null!;
}
