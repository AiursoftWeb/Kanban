using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Notifications;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class NotificationReadTools(
    TemplateDbContext db,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Get the count of unread notifications for the current user")]
    public async Task<string> GetUnreadNotificationCount()
    {
        var userId = currentUser.UserId;
        var count = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();

        return count == 0
            ? "You have no unread notifications."
            : $"You have {count} unread notification(s).";
    }

    [McpServerTool, Description("Get all unread notifications for the current user, with details about the related card, board, and actor")]
    public async Task<string> GetUnreadNotifications(
        [Description("Maximum number of notifications to return (default 20)")] int? limit = null)
    {
        var userId = currentUser.UserId;
        var max = limit ?? 20;

        var notifications = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .Include(n => n.Card!)
                .ThenInclude(c => c.Column)
                    .ThenInclude(col => col.Board)
            .Include(n => n.Board)
            .Include(n => n.ActorUser)
            .OrderByDescending(n => n.CreationTime)
            .Take(max)
            .ToListAsync();

        if (notifications.Count == 0)
            return "You have no unread notifications.";

        var totalCount = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();

        var lines = new List<string>
        {
            totalCount <= max
                ? $"You have {totalCount} unread notification(s):"
                : $"You have {totalCount} unread notification(s). Showing the {notifications.Count} most recent:"
        };

        foreach (var n in notifications)
        {
            var message = NotificationTemplateService.BuildMessage(n);
            var boardName = n.Board?.Name ?? n.Card?.Column.Board.Name ?? "(unknown board)";
            var cardInfo = n.Card != null ? $"Card \"{n.Card.Title}\"" : "";
            var timeAgo = GetRelativeTime(n.CreationTime);

            var line = $"- [#{n.Id}] [{n.Type}] {message}";
            if (!string.IsNullOrWhiteSpace(cardInfo))
                line += $" | {cardInfo}";
            line += $" | Board: \"{boardName}\"";
            line += $" | {timeAgo}";

            lines.Add(line);
        }

        return string.Join("\n", lines);
    }

    private static string GetRelativeTime(DateTime utcTime)
    {
        var diff = DateTime.UtcNow - utcTime;
        return diff.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            < 43200 => $"{(int)diff.TotalDays}d ago",
            _ => utcTime.ToString("yyyy-MM-dd")
        };
    }
}
