using System.ComponentModel;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// MCP tool bridge that exposes registered subagents as callable tools for the main agent.
/// Each subagent appears as a separate tool method so the main agent can invoke them
/// independently via standard tool-use protocol.
/// </summary>
[McpServerToolType]
public class SubagentTool : IScopedDependency
{
    private readonly IEnumerable<ISubagent> _subagents;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<SubagentTool> _logger;

    public SubagentTool(
        IEnumerable<ISubagent> subagents,
        CurrentUserService currentUser,
        ILogger<SubagentTool> logger)
    {
        _subagents = subagents;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Invoke the TaskPlanning subagent to break a complex task into ordered,
    /// implementable steps. The subagent uses FilterCards to gather context
    /// and returns a structured plan.
    /// </summary>
    [McpServerTool, Description(
        "Break down a complex Kanban task into a concrete, implementable sequence of steps. " +
        "Use this when the user's request involves multiple actions, when you need to plan " +
        "before executing to ensure completeness, or when the user explicitly asks for a " +
        "plan or strategy. Provide a detailed description of what the user wants to achieve " +
        "so the planner can gather context and produce an accurate plan.")]
    public async Task<string> TaskPlanning(
        [Description("The complex task or goal to plan. Describe what the user wants to " +
                     "achieve in detail so the planner can search for relevant cards and " +
                     "build an accurate step-by-step plan.")]
        string request,
        CancellationToken ct = default)
    {
        var subagent = _subagents.FirstOrDefault(s => s.Name == "TaskPlanning");
        if (subagent == null)
        {
            _logger.LogError("TaskPlanning subagent not found in DI container");
            return "Error: TaskPlanning subagent is not available.";
        }

        _logger.LogInformation("Invoking TaskPlanning subagent for user '{UserId}'", _currentUser.UserId);

        try
        {
            var result = await subagent.ExecuteAsync(_currentUser.UserId, request, ct);
            _logger.LogInformation("TaskPlanning subagent completed");
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TaskPlanning subagent was cancelled");
            return "Task planning was cancelled due to timeout.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TaskPlanning subagent failed");
            return $"Task planning failed: {ex.Message}";
        }
    }
}
