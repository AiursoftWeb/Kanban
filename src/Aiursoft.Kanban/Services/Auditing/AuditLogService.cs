using System.Security.Claims;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogService(
    AuditLogBuffer buffer,
    AuditLogContext auditLogContext,
    IHttpContextAccessor httpContextAccessor) : IScopedDependency
{
    public void Record(
        string action,
        string category,
        string summary,
        object? details = null,
        string source = "Web",
        string? userId = null,
        string? userName = null)
    {
        auditLogContext.HasSemanticLog = true;
        var context = httpContextAccessor.HttpContext;
        var principal = context?.User;

        buffer.Enqueue(new AuditLog
        {
            EventTime = DateTime.UtcNow,
            UserId = userId ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            UserName = userName ?? principal?.Identity?.Name ?? string.Empty,
            Action = action,
            Category = category,
            Summary = summary,
            Details = details == null ? string.Empty : JsonSerializer.Serialize(details),
            Source = source,
            IpAddress = context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            TraceId = context?.TraceIdentifier ?? string.Empty
        });
    }
}
