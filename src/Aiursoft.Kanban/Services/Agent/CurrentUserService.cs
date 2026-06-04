using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent;

/// <summary>
/// Carries the authenticated user identity during agent tool execution.
///
/// This service is injected into tool method signatures in place of an
/// explicit <c>string userId</c> parameter. Because it is registered in DI,
/// the MCP SDK automatically excludes it from the tool's JSON Schema —
/// the LLM never sees it and cannot supply it.
///
/// AgentService sets <see cref="UserId"/> on the scoped instance before
/// each tool invocation, ensuring tools always act as the authenticated user.
/// </summary>
public class CurrentUserService : IScopedDependency
{
    public string UserId { get; set; } = string.Empty;
}
