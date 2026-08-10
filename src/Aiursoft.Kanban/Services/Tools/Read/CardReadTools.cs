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
    CurrentUserService currentUser,
    TimeProvider timeProvider) : IScopedDependency
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
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

        // Build assigned-to description for query summary
        var assignedDesc = assignedToNorm switch
        {
            null or "" or "me" => $"you ({KanbanAccessService.GetUserDisplayName(await db.Users.FindAsync(userId))})",
            "any" => "anyone",
            _ => $"\"{assignedTo}\""
        };

        if (accessible.Count == 0)
        {
            var noResultDesc = normalizedType switch
            {
                "completed" => $"completed between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}",
                "created" => $"created between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}",
                _ => $"between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}"
            };
            return $"No cards assigned to {assignedDesc}, {noResultDesc}.";
        }

        var ordered = accessible
            .OrderBy(c => c.Column.Board.Name)
            .ThenBy(c => c.Column.Order)
            .ThenBy(c => c.Order)
            .ToList();
        var boardName = boardId.HasValue
            ? $"board \"{(await db.KanbanBoards.FindAsync(boardId.Value))?.Name ?? "(unknown)"}\""
            : "all boards";
        var dateDesc = normalizedType switch
        {
            "completed" => $"completed between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}",
            "created" => $"created between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}",
            _ => $"with activity between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}"
        };

        var lines = new List<string>
        {
            $"Query: cards assigned to {assignedDesc}, {dateDesc}, on {boardName}.",
            $"Found {ordered.Count} card(s):"
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

    [McpServerTool, Description("Advanced card filter — combine keyword, assignee, priority, label, status, and date range in one query. Use for complex tasks like 'find all urgent cards assigned to me with the API label completed this week'. Each filter is optional and combined with AND logic.")]
    public async Task<string> FilterCards(
        [Description("Keyword to search in card title and description")] string? keyword = null,
        [Description("Optional board ID to limit results")] int? boardId = null,
        [Description("Filter by assigned user: 'me' for current user, 'any' for all, or a specific user display name. Omit to skip assignment filter.")] string? assignedTo = null,
        [Description("Priority level: Urgent, High, Medium, Low, or None")] string? priority = null,
        [Description("Label name to filter by (exact match)")] string? label = null,
        [Description("Column status: NotStarted, InProgress, or Completed")] string? columnStatus = null,
        [Description("Date type: 'completed' (ActualEndTime) or 'created' (CreationTime). Required if dateFrom/dateTo are set.")] string? dateType = null,
        [Description("Start date in yyyy-MM-dd format, inclusive. Requires dateType.")] string? dateFrom = null,
        [Description("End date in yyyy-MM-dd format, inclusive. Requires dateType.")] string? dateTo = null)
    {
        var userId = currentUser.UserId;

        var query = db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Include(c => c.CardLabels).ThenInclude(cl => cl.Label)
            .AsQueryable();

        // Board filter
        if (boardId.HasValue)
            query = query.Where(c => c.Column.BoardId == boardId.Value);

        // Keyword filter (title or description)
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            query = query.Where(c =>
                c.Title.ToUpper().Contains(kw) ||
                (c.Description != null && c.Description.ToUpper().Contains(kw)));
        }

        // Assigned-to filter
        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            var assignedNorm = assignedTo.Trim().ToLowerInvariant();
            if (assignedNorm == "me")
                query = query.Where(c => c.AssignedUserId == userId);
            else if (assignedNorm != "any")
            {
                var targetUser = await db.Users
                    .FirstOrDefaultAsync(u =>
                        u.DisplayName.ToUpper() == assignedNorm.ToUpperInvariant() ||
                        (u.UserName != null && u.UserName.ToUpper() == assignedNorm.ToUpperInvariant()) ||
                        (u.Email != null && u.Email.ToUpper() == assignedNorm.ToUpperInvariant()));
                if (targetUser == null)
                    return $"Error: No user found matching \"{assignedTo}\".";
                query = query.Where(c => c.AssignedUserId == targetUser.Id);
            }
        }

        // Priority filter
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<Priority>(priority.Trim(), true, out var prio))
                return $"Error: Invalid priority \"{priority}\". Valid values: Urgent, High, Medium, Low, None.";
            query = query.Where(c => c.Priority == prio);
        }

        // Label filter
        if (!string.IsNullOrWhiteSpace(label))
        {
            var labelNorm = label.Trim().ToUpperInvariant();
            query = query.Where(c =>
                c.CardLabels.Any(cl => cl.Label.Name.ToUpper() == labelNorm));
        }

        // Column status filter
        if (!string.IsNullOrWhiteSpace(columnStatus))
        {
            if (!Enum.TryParse<ColumnStatus>(columnStatus.Trim(), true, out var status))
                return $"Error: Invalid column status \"{columnStatus}\". Valid values: NotStarted, InProgress, Completed.";
            query = query.Where(c => c.Column.ColumnStatus == status);
        }

        // Date range filter
        if (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo))
        {
            if (string.IsNullOrWhiteSpace(dateType))
                return "Error: dateType is required when dateFrom or dateTo is set. Use 'completed' or 'created'.";

            DateTime from, to;
            if (!string.IsNullOrWhiteSpace(dateFrom) && !DateTime.TryParse(dateFrom, out from))
                return $"Error: Invalid dateFrom \"{dateFrom}\". Use yyyy-MM-dd format.";
            if (!string.IsNullOrWhiteSpace(dateTo) && !DateTime.TryParse(dateTo, out to))
                return $"Error: Invalid dateTo \"{dateTo}\". Use yyyy-MM-dd format.";

            from = string.IsNullOrWhiteSpace(dateFrom) ? DateTime.MinValue : DateTime.Parse(dateFrom).Date;
            to = string.IsNullOrWhiteSpace(dateTo) ? DateTime.MaxValue : DateTime.Parse(dateTo).Date.AddDays(1);

            if (from > to)
                return $"Error: dateFrom \"{dateFrom}\" is after dateTo \"{dateTo}\".";

            var dtNorm = dateType.Trim().ToLowerInvariant();
            if (dtNorm == "completed")
                query = query.Where(c => c.ActualEndTime >= from && c.ActualEndTime < to);
            else if (dtNorm == "created")
                query = query.Where(c => c.CreationTime >= from && c.CreationTime < to);
            else
                return $"Error: Invalid dateType \"{dateType}\". Use 'completed' or 'created'.";
        }

        var cards = await query.OrderBy(c => c.Column.Board.Name)
            .ThenBy(c => c.Column.Order)
            .ThenBy(c => c.Order)
            .Take(100)
            .ToListAsync();

        // Access check
        var accessible = new List<KanbanCard>();
        foreach (var card in cards)
        {
            if (await access.HasReadAccess(card.Column.Board, userId))
                accessible.Add(card);
        }

        if (accessible.Count == 0)
        {
            var filterSummary = BuildFilterSummary(
                keyword, assignedTo, priority, label, columnStatus,
                dateType, dateFrom, dateTo, boardId);
            return $"No cards match the specified filters.\n{filterSummary}";
        }

        var lines = new List<string>
        {
            BuildFilterSummary(keyword, assignedTo, priority, label, columnStatus,
                dateType, dateFrom, dateTo, boardId),
            $"Found {accessible.Count} card(s):"
        };
        foreach (var card in accessible)
        {
            var parts = new List<string>();
            parts.Add($"- [#{card.Id}] [{card.Priority}] \"{card.Title}\"");
            parts.Add($"in \"{card.Column.Name}\"");
            parts.Add($"(Board: \"{card.Column.Board.Name}\")");
            var assignee = card.AssignedUserId != null ? "Assigned" : "Unassigned";
            parts.Add($"({assignee})");
            if (card.ActualEndTime.HasValue)
                parts.Add($"Completed: {card.ActualEndTime:yyyy-MM-dd}");
            lines.Add(string.Join(" ", parts));
        }
        return string.Join("\n", lines);
    }

    private static string BuildFilterSummary(
        string? keyword, string? assignedTo, string? priority, string? label,
        string? columnStatus, string? dateType, string? dateFrom, string? dateTo,
        int? boardId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(keyword)) parts.Add($"keyword=\"{keyword}\"");
        if (!string.IsNullOrWhiteSpace(assignedTo)) parts.Add($"assignedTo={assignedTo}");
        if (!string.IsNullOrWhiteSpace(priority)) parts.Add($"priority={priority}");
        if (!string.IsNullOrWhiteSpace(label)) parts.Add($"label=\"{label}\"");
        if (!string.IsNullOrWhiteSpace(columnStatus)) parts.Add($"status={columnStatus}");
        if (!string.IsNullOrWhiteSpace(dateType))
        {
            var range = (dateFrom, dateTo) switch
            {
                (not null, not null) => $"{dateFrom}–{dateTo}",
                (not null, null) => $"since {dateFrom}",
                (null, not null) => $"until {dateTo}",
                _ => ""
            };
            parts.Add($"date={dateType}({range})");
        }
        if (boardId.HasValue) parts.Add($"boardId={boardId}");
        return parts.Count > 0
            ? $"Query: {string.Join(", ", parts)}."
            : "Query: all cards (no filters).";
    }
}
