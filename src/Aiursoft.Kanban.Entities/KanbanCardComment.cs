using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanCardComment
{
    public int Id { get; set; }

    public int CardId { get; set; }

    [ForeignKey(nameof(CardId))]
    public KanbanCard Card { get; set; } = null!;

    [MaxLength(2000)]
    [MinLength(1)]
    public required string Content { get; set; }
    [MaxLength(2000)]
    public string Images { get; set; } = "";

    [StringLength(450)]
    public required string AuthorId { get; set; }

    [ForeignKey(nameof(AuthorId))]
    public User Author { get; set; } = null!;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
