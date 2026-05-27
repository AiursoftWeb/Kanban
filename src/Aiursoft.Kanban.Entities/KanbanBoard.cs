using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanBoard
{
    public int Id { get; set; }

    [MaxLength(100)]
    [MinLength(1)]
    public required string Name { get; set; }

    [StringLength(450)]
    public string? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public bool IsPublic { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    public ICollection<KanbanColumn> Columns { get; set; } = new List<KanbanColumn>();

    public int Order { get; set; }

    [InverseProperty(nameof(BoardShare.Board))]
    public IEnumerable<BoardShare> BoardShares { get; init; } = new List<BoardShare>();
}
