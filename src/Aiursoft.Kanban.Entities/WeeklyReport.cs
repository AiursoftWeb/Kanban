using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class WeeklyReport
{
    [Key]
    public Guid Id { get; init; }

    [StringLength(450)]
    public required string UserId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// The generated report content (bullet-point essay text). May contain markdown formatting.
    /// </summary>
    [MaxLength(8000)]
    public required string Content { get; set; }

    /// <summary>
    /// The Monday of the week this report covers, normalized to midnight UTC.
    /// The date component represents the local (UTC+8) Monday.
    /// </summary>
    public DateTime WeekStart { get; set; }

    /// <summary>
    /// UTC timestamp when this report was generated.
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}
