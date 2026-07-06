using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditActionFilter(
    AuditLogService auditLogService,
    AuditLogContext auditLogContext) : IAsyncActionFilter
{
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

        await auditLogService.RecordAsync(
            action: $"{controller}.{action}",
            category: controller,
            summary: BuildSummary(controller, action, details),
            details: details,
            cancellationToken: context.HttpContext.RequestAborted);
    }

    private static bool IsSuccessful(IActionResult? result)
    {
        return result switch
        {
            null => IsSuccessStatusCode(null),
            EmptyResult => true,
            ViewResult => false,
            ForbidResult => false,
            ChallengeResult => false,
            UnauthorizedResult => false,
            UnauthorizedObjectResult => false,
            NotFoundResult => false,
            NotFoundObjectResult => false,
            BadRequestResult => false,
            BadRequestObjectResult => false,
            RedirectResult => true,
            RedirectToActionResult => true,
            RedirectToRouteResult => true,
            LocalRedirectResult => true,
            JsonResult jsonResult => IsSuccessStatusCode(jsonResult.StatusCode),
            ObjectResult objectResult => IsSuccessStatusCode(objectResult.StatusCode),
            StatusCodeResult statusCodeResult => IsSuccessStatusCode(statusCodeResult.StatusCode),
            ContentResult contentResult => IsSuccessStatusCode(contentResult.StatusCode),
            _ => false
        };
    }

    private static bool IsSuccessStatusCode(int? statusCode)
    {
        return statusCode is null or >= 200 and < 400;
    }

    private static bool IsSafeScalar(string name, object? value)
    {
        if (AuditDetailFilter.IsSensitiveName(name))
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
