using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class BatchWriteTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Create multiple cards at once in a column")]
    [Advice]
    public async Task<string> BatchCreateCards(
        [Description("Target column ID")] int columnId,
        [Description("JSON array of cards with title, optional description, optional assignedUserId, optional plannedStartTime (yyyy-MM-dd), and optional dueDate (yyyy-MM-dd). Example: [{\"title\":\"Card A\",\"description\":\"Desc\",\"assignedUserId\":\"user-id\",\"plannedStartTime\":\"2026-07-01\",\"dueDate\":\"2026-07-15\"},{\"title\":\"Card B\"}]")]
        string cardsJson)
    {
        var userId = currentUser.UserId;
        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        List<BatchCardInput>? inputs;
        try
        {
            inputs = System.Text.Json.JsonSerializer.Deserialize<List<BatchCardInput>>(cardsJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return "Error: Invalid JSON format. Use [{\"title\":\"...\",\"description\":\"...\",\"assignedUserId\":\"...\"}].";
        }

        if (inputs == null || inputs.Count == 0)
            return "Error: No cards specified.";

        // Validate all inputs first so the operation is atomic.
        for (var i = 0; i < inputs.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(inputs[i].Title))
                return $"Error: Card at index {i} has an empty title.";

            var rawAssignee = inputs[i].AssignedUserId;
            var assignee = rawAssignee switch
            {
                null => userId,
                "" => null,
                _ => rawAssignee.Trim()
            };
            if (assignee != null && !await access.CanAssignUserToBoardAsync(column.Board, assignee))
                return $"Error: Assigned user for card at index {i} does not have access to this board.";
        }

        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == columnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        var createdIds = new List<int>();
        foreach (var input in inputs)
        {
            var resolvedAssignee = input.AssignedUserId switch
            {
                null => userId,
                "" => null,
                _ => input.AssignedUserId!.Trim()
            };

            maxOrder++;
            var card = new KanbanCard
            {
                Title = input.Title!.Trim(),
                Description = input.Description?.Trim(),
                Order = maxOrder,
                ColumnId = columnId,
                CreatorUserId = userId,
                AssignedUserId = resolvedAssignee
            };

            if (DateTime.TryParse(input.PlannedStartTime, out var pst))
                card.PlannedStartTime = pst.ToUniversalTime();
            if (DateTime.TryParse(input.DueDate, out var dd))
                card.DueDate = dd.ToUniversalTime();

            db.KanbanCards.Add(card);
            createdIds.Add(card.Id);
        }

        await db.SaveChangesAsync();

        return $"Created {createdIds.Count} card(s) in column \"{column.Name}\".";
    }

    [McpServerTool, Description("Move multiple cards to a target column. Each card gets a sequential position.")]
    [Advice]
    public async Task<string> BatchMoveCards(
        [Description("JSON array of card IDs to move. Example: [1, 2, 3]")]
        string cardIdsJson,
        [Description("Target column ID")] int targetColumnId)
    {
        var userId = currentUser.UserId;
        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == targetColumnId);
        if (column == null) return "Error: Target column not found.";

        List<int>? cardIds;
        try
        {
            cardIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(cardIdsJson);
        }
        catch
        {
            return "Error: Invalid JSON format. Use [1, 2, 3].";
        }

        if (cardIds == null || cardIds.Count == 0)
            return "Error: No card IDs specified.";

        var cards = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Where(c => cardIds.Contains(c.Id))
            .ToListAsync();

        foreach (var card in cards)
        {
            if (!await access.HasEditAccess(card.Column.Board, userId))
                return $"Error: You do not have permission to move card #{card.Id}.";
        }

        var now = DateTime.UtcNow;
        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == targetColumnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        foreach (var card in cards)
        {
            card.ColumnId = targetColumnId;
            maxOrder++;
            card.Order = maxOrder;

            if (column.ColumnStatus == ColumnStatus.InProgress)
                card.ActualStartTime ??= now;
            else if (column.ColumnStatus == ColumnStatus.Completed)
            {
                card.ActualStartTime ??= now;
                card.ActualEndTime = now;
            }
        }

        await db.SaveChangesAsync();

        return $"Moved {cards.Count} card(s) to column \"{column.Name}\".";
    }

    [McpServerTool, Description("Assign multiple cards to a user at once. Leave assignedUserId empty to unassign.")]
    [Advice]
    public async Task<string> BatchAssignCards(
        [Description("JSON array of card IDs. Example: [1, 2, 3]")]
        string cardIdsJson,
        [Description("Target user ID. Leave empty or pass empty string to unassign all cards.")]
        string? assignedUserId)
    {
        var userId = currentUser.UserId;
        List<int>? cardIds;
        try
        {
            cardIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(cardIdsJson);
        }
        catch
        {
            return "Error: Invalid JSON format. Use [1, 2, 3].";
        }

        if (cardIds == null || cardIds.Count == 0)
            return "Error: No card IDs specified.";

        var cards = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Where(c => cardIds.Contains(c.Id))
            .ToListAsync();

        if (cards.Count == 0)
            return "Error: No matching cards found.";

        foreach (var card in cards)
        {
            if (!await access.HasEditAccess(card.Column.Board, userId))
                return $"Error: You do not have permission to modify card #{card.Id}.";
        }

        // Normalize: null or empty means unassign
        var resolvedAssignee = string.IsNullOrWhiteSpace(assignedUserId) ? null : assignedUserId.Trim();

        if (resolvedAssignee != null)
        {
            // Verify assignee has access to each card's board
            foreach (var card in cards)
            {
                if (!await access.CanAssignUserToBoardAsync(card.Column.Board, resolvedAssignee))
                    return $"Error: Target user does not have access to the board of card #{card.Id}.";
            }
        }

        foreach (var card in cards)
        {
            card.AssignedUserId = resolvedAssignee;
        }

        await db.SaveChangesAsync();

        var action = resolvedAssignee != null ? "assigned" : "unassigned";
        return $"{action} {cards.Count} card(s).";
    }

    private class BatchCardInput
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? AssignedUserId { get; set; }
        public string? PlannedStartTime { get; set; }
        public string? DueDate { get; set; }
    }
}
