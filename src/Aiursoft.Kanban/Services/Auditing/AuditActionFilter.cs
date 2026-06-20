using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditActionFilter(
    AuditLogService auditLogService,
    AuditLogContext auditLogContext) : IAsyncActionFilter
{
    private static readonly string[] SensitiveNames =
        ["password", "token", "secret", "content", "description"];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE"))
        {
            await next();
            return;
        }

        var executed = await next();
        if (executed.Exception != null || !IsSuccessful(executed.Result) || auditLogContext.HasSemanticLog) return;

        var controller = context.ActionDescriptor.RouteValues["controller"] ?? "Unknown";
        var action = context.ActionDescriptor.RouteValues["action"] ?? "Unknown";
        var details = context.ActionArguments
            .Where(pair => IsSafeScalar(pair.Key, pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        auditLogService.Record(
            action: $"{controller}.{action}",
            category: controller,
            summary: BuildSummary(controller, action, details),
            details: details);
    }

    private static bool IsSuccessful(IActionResult? result)
    {
        var statusCode = result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => null
        };
        return statusCode is null or >= 200 and < 400;
    }

    private static bool IsSafeScalar(string name, object? value)
    {
        if (SensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
            return false;
        return value is null or string or int or long or bool or Guid or Enum or DateTime;
    }

    private static string BuildSummary(string controller, string action, Dictionary<string, object?> details)
    {
        var target = string.Join(", ", details.Select(pair => $"{pair.Key}={pair.Value}"));
        return string.IsNullOrEmpty(target)
            ? $"Performed {action} in {controller}"
            : $"Performed {action} in {controller}: {target}";
    }
}
