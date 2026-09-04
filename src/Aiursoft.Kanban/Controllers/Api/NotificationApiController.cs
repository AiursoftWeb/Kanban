using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Notifications;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/notifications")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class NotificationApiController(
    TemplateDbContext db,
    UserManager<User> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Unread()
    {
        var userId = CurrentUserId();
        var notifications = await db.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .Include(notification => notification.Comment!)
                .ThenInclude(comment => comment.Author)
            .Include(notification => notification.Card!)
                .ThenInclude(card => card.Column)
                    .ThenInclude(column => column.Board)
            .Include(notification => notification.Board)
            .Include(notification => notification.ActorUser)
            .OrderByDescending(notification => notification.CreationTime)
            .ToListAsync();

        return this.Protocol(new NotificationListResponse
        {
            Code = Code.ResultShown,
            Message = "Unread notifications.",
            UnreadCount = notifications.Count,
            Notifications = notifications.Select(ToDto).ToList()
        });
    }

    [HttpPut("{notificationId:int}/read")]
    public async Task<IActionResult> MarkRead(int notificationId)
    {
        var userId = CurrentUserId();
        var notification = await db.Notifications.FirstOrDefaultAsync(item =>
            item.Id == notificationId && item.UserId == userId && !item.IsRead);
        if (notification == null)
        {
            return this.Protocol(Code.NotFound, "Unread notification not found.");
        }

        notification.IsRead = true;
        await db.SaveChangesAsync();
        return this.Protocol(new AiurResponse
        {
            Code = Code.JobDone,
            Message = "Notification marked as read."
        });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = CurrentUserId();
        var notifications = await db.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync();
        if (notifications.Count == 0)
        {
            return this.Protocol(new AiurResponse
            {
                Code = Code.NoActionTaken,
                Message = "No unread notifications."
            });
        }

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }
        await db.SaveChangesAsync();
        return this.Protocol(new AiurResponse
        {
            Code = Code.JobDone,
            Message = "All notifications marked as read."
        });
    }

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private static NotificationDto ToDto(Notification notification) => new()
    {
        Id = notification.Id,
        CardId = notification.CardId,
        BoardId = notification.BoardId ?? notification.Card?.Column.BoardId,
        CardTitle = notification.Card?.Title,
        BoardName = notification.Board?.Name ?? notification.Card?.Column.Board.Name,
        ColumnName = notification.Card?.Column.Name,
        CommentContent = notification.Comment?.Content,
        Type = notification.Type.ToString(),
        Message = NotificationTemplateService.BuildMessage(notification),
        ActorUserName = GetUserDisplayName(notification.ActorUser) ??
            GetUserDisplayName(notification.Comment?.Author) ??
            "Someone",
        CreationTime = notification.CreationTime
    };

    private static string? GetUserDisplayName(User? user) => user == null
        ? null
        : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
}
