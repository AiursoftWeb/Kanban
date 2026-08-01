using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanCardSubscription
{
    public int CardId { get; set; }
    public KanbanCard Card { get; set; } = null!;

    [StringLength(450)]
    public required string UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
