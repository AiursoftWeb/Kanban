using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class LabelReadTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Search for labels by name")]
    public async Task<string> SearchLabels(
        [Description("Search query (optional, returns all labels if empty)")] string? query)
    {
        var labelsQuery = db.KanbanLabels.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToUpperInvariant();
            labelsQuery = labelsQuery.Where(l => l.Name.ToUpper().Contains(normalized));
        }

        var labels = await labelsQuery
            .OrderByDescending(l => l.CardLabels.Count)
            .ThenBy(l => l.Name)
            .Take(20)
            .ToListAsync();

        if (labels.Count == 0) return "No labels found.";
        return string.Join("\n", labels.Select(l => $"- #{l.Id} \"{l.Name}\" (Color: {l.Color})"));
    }

    [McpServerTool, Description("Get all labels on a specific card")]
    public async Task<string> GetLabelsForCard(
        [Description("Card ID")] int cardId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(column => column.Board)
            .Include(c => c.CardLabels).ThenInclude(cl => cl.Label)
            .FirstOrDefaultAsync(c => c.Id == cardId);

        if (card == null || !await access.HasReadAccess(card.Column.Board, currentUser.UserId))
            return "Error: permission_denied: You do not have permission to view this card.";

        var labels = card.CardLabels.Select(cl => cl.Label).ToList();
        if (labels.Count == 0) return $"Card #{cardId} has no labels.";
        return string.Join("\n", labels.Select(l => $"- #{l.Id} \"{l.Name}\" (Color: {l.Color})"));
    }
}
