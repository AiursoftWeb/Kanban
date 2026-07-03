using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class DailyReport
{
    [Key]
    public Guid Id { get; init; }

    [StringLength(450)]
    public required string UserId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    public DailyReportType ReportType { get; set; }

    /// <summary>
    /// The generated report content (essay text). May contain markdown formatting.
    /// </summary>
    [MaxLength(8000)]
    public required string Content { get; set; }

    /// <summary>
    /// The date this report is for, normalized to midnight UTC.
    /// The date component represents the local (UTC+8) date.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// UTC timestamp when this report was generated.
    /// Used for change detection (regenerate if cards created after this time).
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}
