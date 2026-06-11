using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.NotificationsViewModels;
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
public class NotificationsController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "My Notifications",
        CascadedLinksIcon = "bell",
        CascadedLinksOrder = 4,
        LinkText = "Notifications",
        LinkOrder = 10)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;

        var unreadCount = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();

        var notifications = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .Include(n => n.Comment)
                .ThenInclude(c => c.Author)
            .Include(n => n.Card)
                .ThenInclude(c => c.Column)
                    .ThenInclude(col => col.Board)
            .Include(n => n.ActorUser)
            .OrderByDescending(n => n.CreationTime)
            .ToListAsync();

        var items = notifications.Select(n => new NotificationItem
        {
            Id = n.Id,
            CardId = n.CardId ?? 0,
            BoardId = n.Card?.Column?.BoardId ?? 0,
            CardTitle = n.Card?.Title ?? "(deleted card)",
            BoardName = n.Card?.Column?.Board?.Name ?? "(unknown board)",
            ColumnName = n.Card?.Column?.Name ?? "(unknown column)",
            CommentContent = n.Comment?.Content,
            CommentAuthorName = n.Comment != null ? GetUserDisplayName(n.Comment.Author) : null,
            CommentAuthorInitial = n.Comment != null ? GetUserInitial(n.Comment.Author) : string.Empty,
            Type = n.Type,
            Message = n.Message,
            ActorUserName = GetUserDisplayName(n.ActorUser),
            CreationTime = n.CreationTime
        }).ToList();

        return this.StackView(new IndexViewModel
        {
            Notifications = items,
            UnreadCount = unreadCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = userManager.GetUserId(User)!;
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && !n.IsRead);

        if (notification == null) return NotFound();

        notification.IsRead = true;
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = userManager.GetUserId(User)!;
        var notifications = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in notifications)
            notification.IsRead = true;

        await db.SaveChangesAsync();

        return Ok();
    }

    private static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }

    private static string GetUserInitial(User? user)
    {
        var displayName = GetUserDisplayName(user);
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim()[0].ToString().ToUpperInvariant();
    }
}
