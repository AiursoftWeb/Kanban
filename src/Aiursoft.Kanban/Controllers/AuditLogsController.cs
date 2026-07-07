using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.AuditLogsViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Auditing;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[LimitPerMin]
public class AuditLogsController(
    AuditLogQueryService queryService,
    UserManager<User> userManager) : Controller
{
    private const int PageSize = 50;

    [RenderInNavBar(
        NavGroupName = "Settings",
        NavGroupOrder = 9998,
        CascadedLinksGroupName = "Personal",
        CascadedLinksIcon = "user-circle",
        CascadedLinksOrder = 1,
        LinkText = "My Operation Logs",
        LinkOrder = 4)]
    public async Task<IActionResult> Mine(int page = 1)
    {
        return this.StackView(await BuildModel(userManager.GetUserId(User)!, page, false), "Index");
    }

    [Authorize(Policy = AppPermissionNames.CanReadAuditLogs)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "System",
        CascadedLinksIcon = "settings",
        CascadedLinksOrder = 9999,
        LinkText = "Operation Logs",
        LinkOrder = 2)]
    public async Task<IActionResult> All(int page = 1)
    {
        return this.StackView(await BuildModel(null, page, true), "Index");
    }

    private async Task<IndexViewModel> BuildModel(string? userId, int page, bool showingAllUsers)
    {
        page = Math.Max(1, page);
        var (logs, total) = await queryService.GetLogsAsync(userId, page, PageSize);
        return new IndexViewModel
        {
            Logs = logs,
            Page = page,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)),
            ShowingAllUsers = showingAllUsers,
            Enabled = queryService.Enabled
        };
    }
}
