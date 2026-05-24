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
    public async Task<IActionResult> Index(string status = "incomplete", string? labelIds = null, string labelMode = "any")
    {
        var userId = userManager.GetUserId(User)!;
        var normalizedStatus = NormalizeStatus(status);
        var normalizedLabelMode = NormalizeLabelMode(labelMode);
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

        var orderedCards = filteredCards
            .OrderBy(card => card.Priority)
            .ThenBy(card => card.DueDate == null ? 1 : 0)
            .ThenBy(card => card.DueDate)
            .ThenBy(card => card.Title)
            .ToList();

        return this.StackView(new IndexViewModel
        {
            Cards = orderedCards,
            AvailableLabels = availableLabels,
            SelectedLabelIds = selectedLabelIds,
            SelectedStatus = normalizedStatus,
            SelectedLabelMode = normalizedLabelMode
        });
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "completed" => "completed",
            "in-progress" => "in-progress",
            _ => "incomplete"
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
