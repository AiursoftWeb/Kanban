using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class Notification
{
    public int Id { get; set; }

    public int CardId { get; set; }

    [ForeignKey(nameof(CardId))]
    public KanbanCard Card { get; set; } = null!;

    public int CommentId { get; set; }

    [ForeignKey(nameof(CommentId))]
    public KanbanCardComment Comment { get; set; } = null!;

    [StringLength(450)]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
