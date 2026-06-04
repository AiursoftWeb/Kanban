using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class CardWriteTools(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Create a new card in a column")]
    [Advice]
    public async Task<string> CreateCard(
        [Description("Target column ID")] int columnId,
        [Description("Card title")] string title,
        [Description("Optional card description")] string? description)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(title))
            return "Error: Card title is required.";

        var column = await db.KanbanColumns.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return "Error: Column not found.";
        if (!await access.HasEditAccess(column.Board, userId)) return "Error: You do not have permission to edit this board.";

        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == columnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        var card = new KanbanCard
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            Order = maxOrder + 1,
            ColumnId = columnId,
            AssignedUserId = userId
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();

        return $"Card created: #{card.Id} \"{card.Title}\" in column \"{column.Name}\" (Board: \"{column.Board.Name}\").";
    }

    [McpServerTool, Description("Move a card to a different column and/or position")]
    [Advice]
    public async Task<string> MoveCard(
        [Description("Card ID to move")] int cardId,
        [Description("Target column ID")] int targetColumnId,
        [Description("New position index (0-based) in the target column")] int newOrder)
    {
        var userId = currentUser.UserId;
        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";

        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == targetColumnId);
        if (column == null) return "Error: Target column not found.";

        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var now = DateTime.UtcNow;
        switch (column.ColumnStatus)
        {
            case ColumnStatus.InProgress:
                card.ActualStartTime ??= now;
                card.ActualEndTime = null;
                break;
            case ColumnStatus.Completed:
                card.ActualStartTime ??= now;
                card.ActualEndTime = now;
                break;
        }

        var cardsInColumn = await db.KanbanCards
            .Where(c => c.ColumnId == targetColumnId && c.Id != cardId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var oldColumnName = card.Column.Name;
        card.ColumnId = targetColumnId;

        var allCards = new List<KanbanCard>();
        var idx = 0;
        foreach (var existingCard in cardsInColumn)
        {
            if (idx == newOrder) allCards.Add(card);
            allCards.Add(existingCard);
            idx++;
        }
        if (idx <= newOrder) allCards.Add(card);

        for (var i = 0; i < allCards.Count; i++)
            allCards[i].Order = i;

        await db.SaveChangesAsync();

        return $"Card #{cardId} \"{card.Title}\" moved from \"{oldColumnName}\" to \"{column.Name}\" at position {newOrder}.";
    }

    [McpServerTool, Description("Update card details including title, description, dates, priority, and assignee")]
    [Advice]
    public async Task<string> UpdateCardDetails(
        [Description("Card ID")] int cardId,
        [Description("New title (required)")] string title,
        [Description("New description (optional)")] string? description,
        [Description("Planned start time in yyyy-MM-dd format (optional)")] string? plannedStartTime,
        [Description("Due date in yyyy-MM-dd format (optional)")] string? dueDate,
        [Description("Priority: 0=Urgent, 1=High, 2=Medium, 3=Low, 4=None")] int priority,
        [Description("Assigned user ID (optional, empty string to unassign)")] string? assignedUserId)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(title))
            return "Error: Title is required.";
        if (!Enum.IsDefined(typeof(Priority), priority))
            return "Error: Invalid priority. Use 0-4.";

        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var normalizedAssignedUserId = string.IsNullOrWhiteSpace(assignedUserId) ? null : assignedUserId.Trim();
        if (!await access.CanAssignUserToBoardAsync(card.Column.Board, normalizedAssignedUserId))
            return "Error: Assigned user does not have access to this board.";

        card.Title = title.Trim();
        card.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        card.Priority = (Priority)priority;
        card.AssignedUserId = normalizedAssignedUserId;

        if (DateTime.TryParse(plannedStartTime, out var pst))
            card.PlannedStartTime = pst.ToUniversalTime();
        if (DateTime.TryParse(dueDate, out var dd))
            card.DueDate = dd.ToUniversalTime();

        await db.SaveChangesAsync();

        return $"Card #{cardId} \"{card.Title}\" updated successfully.";
    }

    [McpServerTool, Description("Assign a card to a user")]
    [Advice]
    public async Task<string> AssignCard(
        [Description("Card ID")] int cardId,
        [Description("User ID to assign, or empty to unassign")] string? assignedUserId)
    {
        var userId = currentUser.UserId;
        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        var normalizedAssignedUserId = string.IsNullOrWhiteSpace(assignedUserId) ? null : assignedUserId.Trim();
        if (!await access.CanAssignUserToBoardAsync(card.Column.Board, normalizedAssignedUserId))
            return "Error: Assigned user does not have access to this board.";

        card.AssignedUserId = normalizedAssignedUserId;
        await db.SaveChangesAsync();

        var assigneeDisplay = normalizedAssignedUserId == null
            ? "unassigned"
            : KanbanAccessService.GetUserDisplayName(await userManager.FindByIdAsync(normalizedAssignedUserId));
        return $"Card #{cardId} \"{card.Title}\" assigned to {assigneeDisplay}.";
    }

    [McpServerTool, Description("Update only the priority of a card")]
    [Advice]
    public async Task<string> UpdateCardPriority(
        [Description("Card ID")] int cardId,
        [Description("Priority: 0=Urgent, 1=High, 2=Medium, 3=Low, 4=None")] int priority)
    {
        var userId = currentUser.UserId;
        if (!Enum.IsDefined(typeof(Priority), priority))
            return "Error: Invalid priority. Use 0-4.";

        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await access.HasEditAccess(card.Column.Board, userId))
            return "Error: You do not have permission to edit this board.";

        card.Priority = (Priority)priority;
        await db.SaveChangesAsync();

        return $"Card #{cardId} \"{card.Title}\" priority updated to {(Priority)priority}.";
    }
}
