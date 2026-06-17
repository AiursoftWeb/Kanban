using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// Base class for subagents implementing a mini ReAct (Reasoning + Acting) loop.
/// Each subagent has its own system prompt, filtered tool set, and isolated context
/// (messages are NOT shared with the main agent conversation).
/// </summary>
public abstract class SubagentBase : ISubagent
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ClaudeClient _claudeClient;
    private readonly IServiceProvider _rootServices;
    private readonly ILogger _logger;

    // ── Subagent definition (override in derived classes) ──

    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string SystemPrompt { get; }

    /// <summary>Tool names this subagent is allowed to use. Must be read-only tools.</summary>
    public abstract string[] ToolNames { get; }

    /// <summary>Maximum ReAct loop iterations for this subagent.</summary>
    protected abstract int MaxIterations { get; }

    protected SubagentBase(
        ToolRegistry toolRegistry,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILoggerFactory loggerFactory)
    {
        _toolRegistry = toolRegistry;
        _claudeClient = claudeClient;
        _rootServices = rootServices;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string userId, string input, CancellationToken ct)
    {
        // Subagent has its own isolated conversation — it never mixes with the
        // main agent's messages. This keeps the subagent focused on its task.
        var messages = new List<ClaudeMessage>
        {
            ClaudeMessage.User(input)
        };

        var tools = BuildTools();

        for (int i = 0; i < MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Subagent '{Name}' iteration {Iteration}/{Max}", Name, i + 1, MaxIterations);

            ClaudeResponse response;
            try
            {
                response = await _claudeClient.SendAsync(
                    SystemPrompt, messages, tools, ct, maxTokens: 2048);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subagent '{Name}' LLM call failed on iteration {Iteration}", Name, i + 1);
                return $"Subagent '{Name}' failed: {ex.Message}";
            }

            var toolUses = response.GetToolUses();
            if (toolUses.Count > 0)
            {
                // Append assistant message with tool_call blocks
                var assistantBlocks = new List<ClaudeContentBlock>();
                var text = response.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                    assistantBlocks.Add(ClaudeContentBlock.TextBlock(text));

                foreach (var tu in toolUses)
                {
                    assistantBlocks.Add(ClaudeContentBlock.ToolUse(
                        tu.Id ?? Guid.NewGuid().ToString(),
                        tu.Name ?? "",
                        tu.Input ?? new Dictionary<string, object?>()));
                }
                messages.Add(ClaudeMessage.Assistant(assistantBlocks, response.ReasoningContent));

                // Execute each tool synchronously (no advice — subagents only use read tools)
                foreach (var tu in toolUses)
                {
                    if (string.IsNullOrEmpty(tu.Name)) continue;

                    string toolResult;
                    try
                    {
                        toolResult = await ExecuteTool(userId, tu);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Subagent '{Name}' tool '{ToolName}' failed", Name, tu.Name);
                        toolResult = $"Error executing {tu.Name}: {ex.Message}";
                    }

                    messages.Add(ClaudeMessage.ToolResult(tu.Id ?? "", toolResult));
                }

                continue;
            }

            // No tool calls — subagent produced a final text response
            var finalText = response.GetText();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                _logger.LogDebug("Subagent '{Name}' completed after {Iteration} iteration(s)", Name, i + 1);
                return finalText;
            }

            // Empty response without tool calls — shouldn't normally happen
            _logger.LogWarning("Subagent '{Name}' returned empty response on iteration {Iteration}", Name, i + 1);
            return $"Subagent '{Name}' returned an empty response.";
        }

        // Max iterations reached — ask the LLM one final time using accumulated context
        _logger.LogWarning("Subagent '{Name}' reached max iterations ({Max})", Name, MaxIterations);

        // Try to get one final summary without tools
        try
        {
            var finalResponse = await _claudeClient.SendAsync(
                SystemPrompt + "\n\nYou have reached the maximum number of steps. " +
                "Summarize your findings and produce the best plan you can with the information you have.",
                messages, tools: null, ct, maxTokens: 2048);
            var summary = finalResponse.GetText();
            if (!string.IsNullOrWhiteSpace(summary))
                return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subagent '{Name}' final summary call failed", Name);
        }

        return $"Subagent '{Name}' reached the maximum number of steps without producing a complete plan.";
    }

    /// <summary>
    /// Build the Claude tool definitions filtered to only this subagent's allowed tools.
    /// </summary>
    private List<ClaudeTool> BuildTools()
    {
        return _toolRegistry.AllTools
            .Where(t => ToolNames.Contains(t.ProtocolTool.Name))
            .Select(t =>
            {
                var proto = t.ProtocolTool;
                return new ClaudeTool
                {
                    Name = proto.Name,
                    Description = proto.Description,
                    InputSchema = JsonSerializer.Deserialize<object>(proto.InputSchema.GetRawText())!
                };
            }).ToList();
    }

    /// <summary>
    /// Execute a single read tool on behalf of the subagent. Mirrors the pattern in
    /// AgentService.ExecuteToolWithArgs: creates a scoped container, sets the current
    /// user, serializes arguments, and invokes the MCP tool.
    /// </summary>
    private async Task<string> ExecuteTool(string userId, ClaudeContentBlock toolUse)
    {
        var tool = _toolRegistry.GetTool(toolUse.Name ?? "");
        if (tool == null)
            return $"Error: Unknown tool '{toolUse.Name}'.";

        var rawArgs = toolUse.Input ?? new Dictionary<string, object?>();

        // Sanitize empty strings to null (LLMs often pass "" for optional params)
        var sanitized = new Dictionary<string, object?>();
        foreach (var (key, value) in rawArgs)
        {
            sanitized[key] = value is string s && s.Length == 0 ? null : value;
        }

        using var scope = _rootServices.CreateScope();

        // Set the current user on the scoped service before tool invocation
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = userId;

        var jsonArgs = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in sanitized)
        {
            var json = JsonSerializer.SerializeToElement(value);
            jsonArgs[key] = json;
        }

        var requestParams = new CallToolRequestParams
        {
            Name = tool.ProtocolTool.Name,
            Arguments = jsonArgs
        };

        var request = new RequestContext<CallToolRequestParams>(
            server: NullMcpServer.Instance,
            jsonRpcRequest: new JsonRpcRequest { Method = "tools/call" },
            parameters: requestParams)
        {
            Services = scope.ServiceProvider
        };

        var result = await tool.InvokeAsync(request);
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        return textContent?.Text ?? result.ToString() ?? "Tool executed.";
    }
}
