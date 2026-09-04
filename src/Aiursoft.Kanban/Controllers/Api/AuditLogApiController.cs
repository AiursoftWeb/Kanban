using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Auditing;
using Aiursoft.Kanban.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/audit-logs")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class AuditLogApiController(
    AuditLogQueryService queryService,
    UserManager<User> userManager) : ControllerBase
{
    private const int PageSize = 50;

    [HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] int page = 1)
    {
        page = Math.Max(1, page);
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");
        var (logs, total) = await queryService.GetLogsAsync(userId, page, PageSize);
        return this.Protocol(new OperationLogListResponse
        {
            Code = Code.ResultShown,
            Message = queryService.Enabled
                ? "My operation logs."
                : "Operation logging is not enabled.",
            Logs = logs.Select(ToDto).ToList(),
            CurrentPage = page,
            TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)),
            TotalCount = total,
            Enabled = queryService.Enabled
        });
    }

    private static OperationLogDto ToDto(AuditLog log) => new()
    {
        EventTime = log.EventTime,
        Action = log.Action,
        Category = log.Category,
        Summary = log.Summary,
        Source = log.Source,
        IpAddress = log.IpAddress,
        TraceId = log.TraceId
    };
}
