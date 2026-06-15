using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class CardReadTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Get detailed information about a specific card")]
    public async Task<string> GetCardById(
        [Description("Card ID")] int cardId)
    {
        var userId = currentUser.UserId;
        var card = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Include(c => c.AssignedUser)
            .Include(c => c.CardLabels).ThenInclude(cl => cl.Label)
            .FirstOrDefaultAsync(c => c.Id == cardId);

        if (card == null) return "Card not found.";
        if (!await access.HasReadAccess(card.Column.Board, userId)) return "You do not have access to this board.";

        var labels = card.CardLabels.Select(cl => cl.Label.Name).ToList();
        var labelStr = labels.Count > 0 ? string.Join(", ", labels) : "none";
        var assignee = card.AssignedUser != null
            ? KanbanAccessService.GetUserDisplayName(card.AssignedUser)
            : "unassigned";

        return $"Card #{card.Id} \"{card.Title}\"\n" +
               $"  Description: {card.Description ?? "(none)"}\n" +
               $"  Column: \"{card.Column.Name}\" (#{card.ColumnId})\n" +
               $"  Board: \"{card.Column.Board.Name}\" (#{card.Column.BoardId})\n" +
               $"  Priority: {card.Priority}\n" +
               $"  Assigned: {assignee}\n" +
               $"  Labels: {labelStr}\n" +
               $"  Due Date: {card.DueDate?.ToString("yyyy-MM-dd") ?? "none"}\n" +
               $"  Created: {card.CreationTime:yyyy-MM-dd}";
    }

    [McpServerTool, Description("Search cards by title or description")]
    public async Task<string> SearchCards(
        [Description("Search query")] string query,
        [Description("Optional board ID to limit search. Omit or leave empty to search all boards.")] int? boardId = null)
    {
        var userId = currentUser.UserId;
        var normalized = query.Trim().ToUpperInvariant();
        var cardsQuery = db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Where(c => c.Title.ToUpper().Contains(normalized) ||
                        (c.Description != null && c.Description.ToUpper().Contains(normalized)));

        if (boardId.HasValue)
        {
            cardsQuery = cardsQuery.Where(c => c.Column.BoardId == boardId.Value);
        }

        var cards = await cardsQuery.ToListAsync();

        var accessible = new List<KanbanCard>();
        foreach (var card in cards)
        {
            if (await access.HasReadAccess(card.Column.Board, userId))
                accessible.Add(card);
        }

        if (accessible.Count == 0) return $"No cards found matching \"{query}\".";

        var lines = new List<string> { $"Found {accessible.Count} card(s):" };
        foreach (var card in accessible)
        {
            lines.Add($"- #{card.Id} \"{card.Title}\" in \"{card.Column.Name}\" (Board: \"{card.Column.Board.Name}\")");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get all overdue cards on a board")]
    public async Task<string> GetOverdueCards(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var now = DateTime.UtcNow;
        var cards = await db.KanbanCards
            .Include(c => c.Column)
            .Where(c => c.Column.BoardId == boardId &&
                        c.DueDate.HasValue &&
                        c.DueDate.Value < now &&
                        c.Column.ColumnStatus != ColumnStatus.Completed)
            .OrderBy(c => c.DueDate)
            .ToListAsync();

        if (cards.Count == 0) return "No overdue cards.";

        var lines = new List<string> { $"Found {cards.Count} overdue card(s):" };
        foreach (var card in cards)
        {
            var daysOverdue = (now - card.DueDate!.Value).Days;
            lines.Add($"- #{card.Id} \"{card.Title}\" (Due: {card.DueDate:yyyy-MM-dd}, {daysOverdue} days overdue, Column: \"{card.Column.Name}\")");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get cards filtered by priority level on a board")]
    public async Task<string> GetCardsByPriority(
        [Description("Board ID")] int boardId,
        [Description("Priority level: Urgent, High, Medium, Low, or None")] string priority)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        if (!Enum.TryParse<Priority>(priority, true, out var priorityEnum))
            return $"Invalid priority \"{priority}\". Valid values: Urgent, High, Medium, Low, None.";

        var cards = await db.KanbanCards
            .Include(c => c.Column)
            .Where(c => c.Column.BoardId == boardId && c.Priority == priorityEnum)
            .OrderBy(c => c.Order)
            .ToListAsync();

        if (cards.Count == 0) return $"No {priority} priority cards.";

        var lines = new List<string> { $"Found {cards.Count} {priority} priority card(s):" };
        foreach (var card in cards)
        {
            lines.Add($"- #{card.Id} \"{card.Title}\" in \"{card.Column.Name}\"");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get all unassigned cards on a board")]
    public async Task<string> GetUnassignedCards(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var cards = await db.KanbanCards
            .Include(c => c.Column)
            .Where(c => c.Column.BoardId == boardId && c.AssignedUserId == null)
            .OrderBy(c => c.Order)
            .ToListAsync();

        if (cards.Count == 0) return "No unassigned cards.";

        var lines = new List<string> { $"Found {cards.Count} unassigned card(s):" };
        foreach (var card in cards)
        {
            lines.Add($"- #{card.Id} \"{card.Title}\" in \"{card.Column.Name}\"");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get cards that have a specific label")]
    public async Task<string> GetCardsByLabel(
        [Description("Label name to search for")] string labelName,
        [Description("Optional board ID to limit search. Omit or leave empty to search all boards.")] int? boardId = null)
    {
        var userId = currentUser.UserId;
        var normalized = labelName.Trim().ToUpperInvariant();
        var cardsQuery = db.KanbanCardLabels
            .Include(cl => cl.Card).ThenInclude(c => c.Column).ThenInclude(col => col.Board)
            .Include(cl => cl.Label)
            .Where(cl => cl.Label.Name.ToUpper() == normalized);

        if (boardId.HasValue)
        {
            cardsQuery = cardsQuery.Where(cl => cl.Card.Column.BoardId == boardId.Value);
        }

        var cardLabels = await cardsQuery.ToListAsync();

        var accessible = new List<(KanbanCard Card, string BoardName)>();
        foreach (var cl in cardLabels)
        {
            if (await access.HasReadAccess(cl.Card.Column.Board, userId))
                accessible.Add((cl.Card, cl.Card.Column.Board.Name));
        }

        if (accessible.Count == 0) return $"No cards found with label \"{labelName}\".";

        var lines = new List<string> { $"Found {accessible.Count} card(s) with label \"{labelName}\":" };
        foreach (var (card, boardName) in accessible)
        {
            lines.Add($"- #{card.Id} \"{card.Title}\" in \"{card.Column.Name}\" (Board: \"{boardName}\")");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get cards assigned to the current user across all boards, with optional status and board filters")]
    public async Task<string> GetMyTasks(
        [Description("Status filter: incomplete (default), not-started, in-progress, completed, or all")] string? status = null,
        [Description("Optional board ID to limit results to a specific board. Omit or leave empty to search all boards.")] int? boardId = null)
    {
        var userId = currentUser.UserId;
        var normalizedStatus = (status?.Trim().ToLowerInvariant()) switch
        {
            "all" => "all",
            "completed" => "completed",
            "in-progress" => "in-progress",
            "not-started" => "not-started",
            _ => "incomplete"
        };

        var query = db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Include(c => c.AssignedUser)
            .Where(c => c.AssignedUserId == userId);

        if (boardId.HasValue)
            query = query.Where(c => c.Column.BoardId == boardId.Value);

        query = normalizedStatus switch
        {
            "not-started" => query.Where(c => c.Column.ColumnStatus == ColumnStatus.NotStarted),
            "in-progress" => query.Where(c => c.Column.ColumnStatus == ColumnStatus.InProgress),
            "completed" => query.Where(c => c.Column.ColumnStatus == ColumnStatus.Completed),
            "all" => query,
            _ => query.Where(c =>
                c.Column.ColumnStatus == ColumnStatus.NotStarted ||
                c.Column.ColumnStatus == ColumnStatus.InProgress)
        };

        var cards = await query.ToListAsync();

        var ordered = cards
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.DueDate == null ? 1 : 0)
            .ThenBy(c => c.DueDate)
            .ThenBy(c => c.Title)
            .ToList();

        if (ordered.Count == 0)
        {
            var scope = boardId.HasValue ? " on this board" : "";
            return normalizedStatus switch
            {
                "all" => $"You have no cards assigned to you{scope}.",
                "completed" => $"You have no completed cards{scope}.",
                "in-progress" => $"You have no in-progress cards{scope}.",
                "not-started" => $"You have no not-started cards{scope}.",
                _ => $"You have no incomplete cards{scope}."
            };
        }

        var lines = new List<string> { $"Found {ordered.Count} card(s) assigned to you:" };
        foreach (var card in ordered)
        {
            var dueStr = card.DueDate.HasValue ? $" (Due: {card.DueDate:yyyy-MM-dd})" : "";
            lines.Add($"- [#{card.Id}] [{card.Priority}] \"{card.Title}\" in \"{card.Column.Name}\" (Board: \"{card.Column.Board.Name}\"){dueStr}");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get cards within a date range, filtered by assigned user. Essential for weekly reports — defaults to cards assigned to you. Use assignedTo='any' to see everyone's work.")]
    public async Task<string> GetCardsByDateRange(
        [Description("Start date in yyyy-MM-dd format, inclusive")] string startDate,
        [Description("End date in yyyy-MM-dd format, inclusive")] string endDate,
        [Description("Optional board ID to limit results. Omit or leave empty to search all boards.")] int? boardId = null,
        [Description("Which date field to filter: 'completed' (ActualEndTime, use for weekly summaries), 'created' (CreationTime), or omit/empty for either")] string? dateType = null,
        [Description("Filter by assigned user: 'me' or omit for current user (default), 'any' for all users, or a specific user display name.")] string? assignedTo = null)
    {
        var userId = currentUser.UserId;

        if (!DateTime.TryParse(startDate, out var start))
            return $"Error: Invalid start date \"{startDate}\". Use yyyy-MM-dd format.";
        if (!DateTime.TryParse(endDate, out var end))
            return $"Error: Invalid end date \"{endDate}\". Use yyyy-MM-dd format.";
        if (start > end)
            return $"Error: Start date \"{startDate}\" is after end date \"{endDate}\".";

        // Make end date inclusive (end of day)
        var endInclusive = end.Date.AddDays(1);

        var normalizedType = (dateType?.Trim().ToLowerInvariant()) switch
        {
            "completed" => "completed",
            "created" => "created",
            _ => "any"
        };

        var query = db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .AsQueryable();

        if (boardId.HasValue)
        {
            query = query.Where(c => c.Column.BoardId == boardId.Value);
        }

        query = normalizedType switch
        {
            "completed" => query.Where(c => c.ActualEndTime >= start.Date && c.ActualEndTime < endInclusive),
            "created" => query.Where(c => c.CreationTime >= start.Date && c.CreationTime < endInclusive),
            _ => query.Where(c =>
                (c.ActualEndTime.HasValue && c.ActualEndTime >= start.Date && c.ActualEndTime < endInclusive) ||
                (c.CreationTime >= start.Date && c.CreationTime < endInclusive))
        };

        // Resolve assigned-to filter: default to current user
        var assignedToNorm = assignedTo?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(assignedToNorm) || assignedToNorm == "me")
        {
            query = query.Where(c => c.AssignedUserId == userId);
        }
        else if (assignedToNorm != "any")
        {
            // Try to find user by display name, username, or email
            var targetUser = await db.Users
                .FirstOrDefaultAsync(u =>
                    u.DisplayName.ToUpper() == assignedToNorm.ToUpperInvariant() ||
                    (u.UserName != null && u.UserName.ToUpper() == assignedToNorm.ToUpperInvariant()) ||
                    (u.Email != null && u.Email.ToUpper() == assignedToNorm.ToUpperInvariant()));
            if (targetUser == null)
                return $"Error: No user found matching \"{assignedTo}\".";
            query = query.Where(c => c.AssignedUserId == targetUser.Id);
        }
        // "any" → no filter

        var cards = await query.ToListAsync();

        var accessible = new List<KanbanCard>();
        foreach (var card in cards)
        {
            if (await access.HasReadAccess(card.Column.Board, userId))
                accessible.Add(card);
        }

        if (accessible.Count == 0)
        {
            var filterDesc = normalizedType switch
            {
                "completed" => "completed ",
                "created" => "created ",
                _ => ""
            };
            return $"No {filterDesc}cards found between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}.";
        }

        var ordered = accessible
            .OrderBy(c => c.Column.Board.Name)
            .ThenBy(c => c.Column.Order)
            .ThenBy(c => c.Order)
            .ToList();

        var lines = new List<string>
        {
            $"Found {ordered.Count} card(s) between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}:"
        };
        foreach (var card in ordered)
        {
            var completedStr = card.ActualEndTime.HasValue
                ? $" Completed: {card.ActualEndTime:yyyy-MM-dd}"
                : "";
            lines.Add($"- [#{card.Id}] \"{card.Title}\" in \"{card.Column.Name}\" (Board: \"{card.Column.Board.Name}\"){completedStr}");
        }
        return string.Join("\n", lines);
    }
}
