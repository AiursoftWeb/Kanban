using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanLabel
{
    public int Id { get; set; }

    [MaxLength(100)]
    [MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(7)]
    [MinLength(7)]
    public string Color { get; set; } = "#6B7280";

    public List<KanbanCardLabel> CardLabels { get; set; } = [];
}
