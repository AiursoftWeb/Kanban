// ============================================================
// CardDetailViewModel — ViewModel for /Cards/{id} detail page
// ============================================================

using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.CardViewModels;

public class CardDetailViewModel : UiStackLayoutViewModel
{
    public CardDetailViewModel()
    {
        PageTitle = "Card Details";
    }

    // Card identity
    public int CardId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }

    // Column & Board context
    public int ColumnId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public int ReturnBoardId { get; set; }

    // Permissions
    public bool CanEdit { get; set; }

    // Assignee
    public string? AssigneeId { get; set; }
    public string AssigneeName { get; set; } = string.Empty;
    public string AssigneeInitial { get; set; } = string.Empty;
    public string? AssigneeAvatarUrl { get; set; }

    // Creator
    public string CreatorName { get; set; } = string.Empty;
    public string CreatorInitial { get; set; } = string.Empty;
    public string? CreatorAvatarUrl { get; set; }
    public string CreationTime { get; set; } = string.Empty;

    // Dates
    public string DueDate { get; set; } = string.Empty;
    public string PlannedStartDate { get; set; } = string.Empty;
    public string ActualStartDate { get; set; } = string.Empty;
    public string ActualEndDate { get; set; } = string.Empty;

    // Recurrence
    public bool IsRecurring { get; set; }
    public string RecurrenceInterval { get; set; } = string.Empty;
    public int RecurrenceUnit { get; set; }

    // Labels
    public List<LabelViewModel> Labels { get; set; } = [];

    // Comments
    public List<CommentViewModel> Comments { get; set; } = [];

    // Move / transfer options
    public List<ColumnOptionViewModel> Columns { get; set; } = [];
    public List<BoardOptionViewModel> AvailableBoards { get; set; } = [];
}

public class LabelViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public class CommentViewModel
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorInitial { get; set; } = string.Empty;
    public string? AuthorAvatarUrl { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public bool CanDelete { get; set; }
}

public class ColumnOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BoardOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
