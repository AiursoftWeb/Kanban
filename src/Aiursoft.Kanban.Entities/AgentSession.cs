using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class AgentSession
{
    [Key]
    public Guid Id { get; init; }

    [Required]
    [StringLength(450)]
    public required string UserId { get; init; }

    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public int? BoardId { get; init; }

    [JsonIgnore]
    [ForeignKey(nameof(BoardId))]
    public KanbanBoard? Board { get; set; }

    [Required]
    [StringLength(160)]
    public required string Title { get; set; }

    [Required]
    [StringLength(32)]
    public required string State { get; set; }

    [StringLength(4000)]
    public string? ErrorMessage { get; set; }

    [Required]
    public string TranscriptJson { get; set; } = "[]";

    public DateTime CreationTime { get; init; }
    public DateTime LastActivity { get; set; }
}
