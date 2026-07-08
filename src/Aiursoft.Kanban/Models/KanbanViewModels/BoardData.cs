// ============================================================
// BoardData DTOs — serialized to JSON for the KanbanBoard TS module
// ============================================================

using System.Text.Json.Serialization;

namespace Aiursoft.Kanban.Models.KanbanViewModels;

/// <summary>
/// Full board data passed to the KanbanBoard TypeScript module.
/// Serialized with System.Text.Json (camelCase).
/// </summary>
public class BoardData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public List<ColumnData> Columns { get; set; } = [];
}

public class ColumnData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    /// <summary>CSS dot class: dot-blue, dot-orange, etc.</summary>
    public string DotClass { get; set; } = string.Empty;

    public string Status { get; set; } = "NotStarted";
    public int Order { get; set; }
    public List<CardSummary> Cards { get; set; } = [];
}

public class CardSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = "None";
    public string? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public string? PlannedStartDate { get; set; }
    public string? ActualStartDate { get; set; }
    public string? ActualEndDate { get; set; }
    public UserSummary? Assignee { get; set; }
    public UserSummary? Creator { get; set; }
    public string? CreationTime { get; set; }
    public List<LabelSummary> Labels { get; set; } = [];
    public int CommentCount { get; set; }
    public bool IsRecurring { get; set; }
    public int? RecurrenceInterval { get; set; }
    public int RecurrenceUnit { get; set; }
    public string? Description { get; set; }
}

public class UserSummary
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AvatarUrl { get; set; }
}

public class LabelSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
