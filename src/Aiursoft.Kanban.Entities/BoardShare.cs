using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class BoardShare
{
    [Key]
    public Guid Id { get; init; }

    public required int BoardId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(BoardId))]
    [NotNull]
    public KanbanBoard? Board { get; set; }

    /// <summary>
    /// 分享给特定用户的ID。如果为null，表示此分享不是针对特定用户的。
    /// </summary>
    [StringLength(450)]
    public string? SharedWithUserId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(SharedWithUserId))]
    public User? SharedWithUser { get; set; }

    /// <summary>
    /// 分享给特定角色的ID。如果为null，表示此分享不是针对特定角色的。
    /// </summary>
    [StringLength(450)]
    public string? SharedWithRoleId { get; set; }

    /// <summary>
    /// 分享权限：ReadOnly 或 Editable
    /// </summary>
    public required SharePermission Permission { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
