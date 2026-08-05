using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class KanbanCard
{
    public const int DescriptionMaxLength = 160_000;

    public int Id { get; set; }

    [MaxLength(200)]
    [MinLength(1)]
    public required string Title { get; set; }

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    public int Order { get; set; }

    public int ColumnId { get; set; }
    public KanbanColumn Column { get; set; } = null!;

    public Priority Priority { get; set; } = Priority.None;

    [StringLength(450)]
    public string? AssignedUserId { get; set; }

    [ForeignKey(nameof(AssignedUserId))]
    public User? AssignedUser { get; set; }

    [StringLength(450)]
    public string? CreatorUserId { get; set; }

    [ForeignKey(nameof(CreatorUserId))]
    public User? CreatorUser { get; set; }

    public List<KanbanCardLabel> CardLabels { get; set; } = [];

    public List<KanbanCardSubscription> Subscriptions { get; set; } = [];

    public DateTime? PlannedStartTime { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? ActualStartTime { get; set; }

    public DateTime? ActualEndTime { get; set; }

    public int? RecurrenceInterval { get; set; }

    public RecurrenceUnit RecurrenceUnit { get; set; } = RecurrenceUnit.None;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public byte[]? Embedding { get; set; }

    public DateTime LastEmbeddedAt { get; set; } = DateTime.MinValue;
}
