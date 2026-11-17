using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanCategory
{
    public int Id { get; set; }
    [MaxLength(100), MinLength(1)]
    public required string Name { get; set; }
    public int Order { get; set; }
    public bool IsDefault { get; set; }
    public int BoardId { get; set; }
    public KanbanBoard Board { get; set; } = null!;
    public ICollection<KanbanCard> Cards { get; set; } = new List<KanbanCard>();
}
