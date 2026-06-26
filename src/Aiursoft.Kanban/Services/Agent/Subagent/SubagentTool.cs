using System.ComponentModel;
using Aiursoft.Scanner.Abstractions;
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
            _logger.LogError("[SubagentTool] TaskPlanning subagent not found in DI container");
            return "Error: TaskPlanning subagent is not available.";
        }

        _logger.LogInformation(
            "[SubagentTool] Dispatching to TaskPlanning subagent | RequestLen={ReqLen} chars | User={UserId}",
            request.Length, _currentUser.UserId);

        try
        {
            var result = await subagent.ExecuteAsync(_currentUser.UserId, request, ct);

            _logger.LogInformation(
                "[SubagentTool] TaskPlanning returned | ResultLen={ResultLen} chars | Result preview: {Preview}",
                result.Length, Truncate(result, 500));

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[SubagentTool] TaskPlanning CANCELLED (timeout or user abort)");
            return "Task planning was cancelled due to timeout.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SubagentTool] TaskPlanning FAILED");
            return $"Task planning failed: {ex.Message}";
        }
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }
}
