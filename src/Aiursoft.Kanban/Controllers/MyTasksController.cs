using Aiursoft.Kanban.Authorization;
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
    UserManager<User> userManager,
    IAuthorizationService authorizationService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "My Tasks",
        CascadedLinksIcon = "list-checks",
        CascadedLinksOrder = 3,
        LinkText = "My Tasks",
        LinkOrder = 10)]
    public async Task<IActionResult> Index(string? targetUserId = null, string status = "incomplete", string? labelIds = null, string labelMode = "any", string sort = "planned-end-desc")
    {
        var currentUserId = userManager.GetUserId(User)!;
        var queryUserId = string.IsNullOrWhiteSpace(targetUserId) ? currentUserId : targetUserId;

        var hasViewAnyUserTasksPermission = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanViewAnyUserTasks)).Succeeded;

        if (queryUserId != currentUserId && !hasViewAnyUserTasksPermission)
        {
            return Forbid();
        }

        var targetUser = await userManager.FindByIdAsync(queryUserId);
        if (targetUser == null)
        {
            return NotFound();
        }

        List<User>? availableUsers = null;
        if (hasViewAnyUserTasksPermission)
        {
            availableUsers = await db.Users.OrderBy(u => u.DisplayName).ToListAsync();
        }

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
            .Where(card => card.AssignedUserId == queryUserId);

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
            HasViewAnyUserTasksPermission = hasViewAnyUserTasksPermission,
            AvailableUsers = availableUsers,
            Cards = orderedCards,
            TargetUser = targetUser,
            IsViewingOtherUser = queryUserId != currentUserId,
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
            "planned-start-asc" => "planned-start-asc",
            "planned-start-desc" => "planned-start-desc",
            "planned-end-asc" => "planned-end-asc",
            "planned-end-desc" => "planned-end-desc",
            "due-date-asc" => "planned-end-asc",
            "due-date-desc" => "planned-end-desc",
            "actual-start-asc" => "actual-start-asc",
            "actual-start-desc" => "actual-start-desc",
            "actual-end-asc" => "actual-end-asc",
            "actual-end-desc" => "actual-end-desc",
            "priority-asc" => "priority-asc",
            "priority-desc" => "priority-desc",
            "creation-desc" => "creation-desc",
            "creation-asc" => "creation-asc",
            "title-asc" => "title-asc",
            "title-desc" => "title-desc",
            _ => "planned-end-desc"
        };
    }

    private static List<KanbanCard> ApplySort(IEnumerable<KanbanCard> cards, string sort)
    {
        var now = DateTime.UtcNow;
        return sort switch
        {
            "planned-start-asc" => cards
                .OrderBy(card => card.PlannedStartTime == null ? 1 : 0)
                .ThenBy(card => card.PlannedStartTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "planned-start-desc" => cards
                .OrderBy(card => card.PlannedStartTime == null ? 1 : 0)
                .ThenByDescending(card => card.PlannedStartTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "planned-end-asc" => cards
                .OrderBy(card => card.DueDate == null ? 1 : 0)
                .ThenBy(card => card.DueDate)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
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
            "actual-end-asc" => cards
                .OrderBy(card => card.ActualEndTime == null ? 1 : 0)
                .ThenBy(card => card.ActualEndTime)
                .ThenBy(card => card.Priority)
                .ThenBy(card => card.Title)
                .ToList(),
            "actual-end-desc" => cards
                .OrderBy(card => card.ActualEndTime == null ? 1 : 0)
                .ThenByDescending(card => card.ActualEndTime)
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
            _ => cards // planned-end-desc (default): overdue first, then upcoming, then no due date
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
