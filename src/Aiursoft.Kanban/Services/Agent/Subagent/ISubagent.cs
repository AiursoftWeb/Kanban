namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// A subagent is a mini-agent with its own system prompt, tool set, and context.
/// It runs independently of the main agent and returns a result.
/// Subagents can be registered as MCP tools for the main agent to call,
/// or triggered conditionally by the main agent loop.
/// </summary>
public interface ISubagent
{
    /// <summary>Unique name used to look up this subagent.</summary>
    string Name { get; }

    /// <summary>Human-readable description shown to the main agent as tool description.</summary>
    string Description { get; }

    /// <summary>
    /// Execute the subagent with the given input and return its final text result.
    /// </summary>
    /// <param name="userId">The authenticated user identity.</param>
    /// <param name="input">The task or question for the subagent to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subagent's final text output.</returns>
    Task<string> ExecuteAsync(string userId, string input, CancellationToken ct);
}
