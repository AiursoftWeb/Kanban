using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.MyTasksViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[LimitPerMin]
public class MyTasksController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "My Tasks",
        CascadedLinksIcon = "list-checks",
        CascadedLinksOrder = 3,
        LinkText = "My Tasks",
        LinkOrder = 10)]
    public async Task<IActionResult> Index(string status = "incomplete", string? labelIds = null, string labelMode = "any", string sort = "due-date-desc")
    {
        var userId = userManager.GetUserId(User)!;
        var normalizedStatus = NormalizeStatus(status);
        var normalizedLabelMode = NormalizeLabelMode(labelMode);
        var normalizedSort = NormalizeSort(sort);
        var selectedLabelIds = ParseLabelIds(labelIds);

        var cardsQuery = db.KanbanCards
            .Include(card => card.CardLabels)
                .ThenInclude(link => link.Label)
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .Include(card => card.AssignedUser)
            .Where(card => card.AssignedUserId == userId);

        cardsQuery = normalizedStatus switch
        {
            "not-started" => cardsQuery.Where(card => card.Column.ColumnStatus == ColumnStatus.NotStarted),
            "in-progress" => cardsQuery.Where(card => card.Column.ColumnStatus == ColumnStatus.InProgress),
            "completed" => cardsQuery.Where(card => card.Column.ColumnStatus == ColumnStatus.Completed),
            "all" => cardsQuery,
            _ => cardsQuery.Where(card =>
                card.Column.ColumnStatus == ColumnStatus.NotStarted ||
                card.Column.ColumnStatus == ColumnStatus.InProgress)
        };

        var statusFilteredCards = await cardsQuery.ToListAsync();

        var availableLabels = statusFilteredCards
            .SelectMany(card => card.CardLabels)
            .GroupBy(link => link.LabelId)
            .Select(group => new LabelFilterViewModel
            {
                Id = group.Key,
                Name = group.First().Label.Name,
                Color = group.First().Label.Color,
                UsageCount = group.Count()
            })
            .OrderByDescending(label => label.UsageCount)
            .ThenBy(label => label.Name)
            .ToList();

        IEnumerable<KanbanCard> filteredCards = statusFilteredCards;
        if (selectedLabelIds.Count > 0)
        {
            filteredCards = normalizedLabelMode == "all"
                ? filteredCards.Where(card => selectedLabelIds.All(labelId => card.CardLabels.Any(link => link.LabelId == labelId)))
                : filteredCards.Where(card => card.CardLabels.Any(link => selectedLabelIds.Contains(link.LabelId)));
        }

        var orderedCards = ApplySort(filteredCards, normalizedSort);

        return this.StackView(new IndexViewModel
        {
            Cards = orderedCards,
            AvailableLabels = availableLabels,
            SelectedLabelIds = selectedLabelIds,
            SelectedStatus = normalizedStatus,
            SelectedLabelMode = normalizedLabelMode,
            SelectedSort = normalizedSort
        });
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "completed" => "completed",
            "in-progress" => "in-progress",
            "not-started" => "not-started",
            _ => "incomplete"
        };
    }

    private static string NormalizeSort(string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "priority-asc" => "priority-asc",
            "priority-desc" => "priority-desc",
            "due-date-asc" => "due-date-asc",
            "actual-start-desc" => "actual-start-desc",
            "actual-start-asc" => "actual-start-asc",
            "creation-desc" => "creation-desc",
            "creation-asc" => "creation-asc",
            "title-asc" => "title-asc",
            "title-desc" => "title-desc",
            _ => "due-date-desc"
        };
    }

    private static List<KanbanCard> ApplySort(IEnumerable<KanbanCard> cards, string sort)
    {
        var now = DateTime.UtcNow;
        return sort switch
        {
            "priority-asc" => cards
                .OrderBy(card => card.Priority)
                .ThenBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ThenBy(card => card.Title)
                .ToList(),
            "priority-desc" => cards
                .OrderByDescending(card => card.Priority)
                .ThenBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ThenBy(card => card.Title)
                .ToList(),
            "due-date-asc" => cards
                .OrderBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "actual-start-desc" => cards
                .OrderBy(card => card.ActualStartTime == null ? 1 : 0)
                .ThenByDescending(card => card.ActualStartTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "actual-start-asc" => cards
                .OrderBy(card => card.ActualStartTime == null ? 1 : 0)
                .ThenBy(card => card.ActualStartTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "creation-desc" => cards
                .OrderByDescending(card => card.CreationTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "creation-asc" => cards
                .OrderBy(card => card.CreationTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "title-asc" => cards
                .OrderBy(card => card.Title)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ToList(),
            "title-desc" => cards
                .OrderByDescending(card => card.Title)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ToList(),
            _ => cards // due-date-desc (default): overdue first, then upcoming, then no due date
                .OrderBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate.HasValue && card.DueDate.Value < now ? 0 : 1)
                .ThenBy(card => card.DueDate)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList()
        };
    }
    private static string NormalizeLabelMode(string? labelMode)
    {
        return labelMode?.Trim().ToLowerInvariant() == "all" ? "all" : "any";
    }

    private static HashSet<int> ParseLabelIds(string? labelIds)
    {
        if (string.IsNullOrWhiteSpace(labelIds)) return [];

        return labelIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var parsedId) ? parsedId : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
    }
}
