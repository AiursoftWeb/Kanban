using System.Diagnostics;
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
        var totalStopwatch = Stopwatch.StartNew();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        _logger.LogInformation(
            "[Subagent:{Name}] Started | InputLength={InputLen} chars | Tools=[{ToolList}] | MaxIterations={MaxIter}",
            Name, input.Length, string.Join(", ", ToolNames), MaxIterations);

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

            var iterStopwatch = Stopwatch.StartNew();

            _logger.LogDebug("[Subagent:{Name}] Iteration {Iter}/{Max} starting", Name, i + 1, MaxIterations);

            ClaudeResponse response;
            try
            {
                response = await _claudeClient.SendAsync(
                    SystemPrompt, messages, tools, ct, maxTokens: 2048);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Subagent:{Name}] LLM call FAILED at iteration {Iter}/{Max} after {Elapsed}ms",
                    Name, i + 1, MaxIterations, iterStopwatch.ElapsedMilliseconds);
                return $"Subagent '{Name}' failed: {ex.Message}";
            }

            iterStopwatch.Stop();

            // Track token usage for observability
            var iterInputTokens = response.Usage?.InputTokens ?? 0;
            var iterOutputTokens = response.Usage?.OutputTokens ?? 0;
            totalInputTokens += iterInputTokens;
            totalOutputTokens += iterOutputTokens;

            _logger.LogInformation(
                "[Subagent:{Name}] Iteration {Iter}/{Max} | LLM: {InputTok}+{OutputTok} tokens | {Elapsed}ms | StopReason={StopReason}",
                Name, i + 1, MaxIterations, iterInputTokens, iterOutputTokens,
                iterStopwatch.ElapsedMilliseconds, response.StopReason);

            var toolUses = response.GetToolUses();
            if (toolUses.Count > 0)
            {
                var thoughtText = response.GetText();
                if (!string.IsNullOrWhiteSpace(thoughtText))
                {
                    _logger.LogDebug("[Subagent:{Name}] Thought: {Thought}",
                        Name, Truncate(thoughtText, 300));
                }

                // Append assistant message with tool_call blocks
                var assistantBlocks = new List<ClaudeContentBlock>();
                if (!string.IsNullOrWhiteSpace(thoughtText))
                    assistantBlocks.Add(ClaudeContentBlock.TextBlock(thoughtText));

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

                    var toolStopwatch = Stopwatch.StartNew();
                    string toolResult;
                    try
                    {
                        toolResult = await ExecuteTool(userId, tu);
                        toolStopwatch.Stop();

                        _logger.LogInformation(
                            "[Subagent:{Name}] Tool call: {ToolName}({Args}) → {ResultLen} chars | {Elapsed}ms",
                            Name, tu.Name, SummarizeArgs(tu.Input), toolResult.Length,
                            toolStopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        toolStopwatch.Stop();
                        _logger.LogError(ex,
                            "[Subagent:{Name}] Tool call FAILED: {ToolName} | {Elapsed}ms",
                            Name, tu.Name, toolStopwatch.ElapsedMilliseconds);
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
                totalStopwatch.Stop();
                _logger.LogInformation(
                    "[Subagent:{Name}] COMPLETED | Iterations={Iter} | Total tokens: {InTok}+{OutTok} | Output={OutLen} chars | Total {TotalMs}ms\n" +
                    "── Subagent Output ──\n{Output}",
                    Name, i + 1, totalInputTokens, totalOutputTokens,
                    finalText.Length, totalStopwatch.ElapsedMilliseconds,
                    Truncate(finalText, 2000));
                return finalText;
            }

            // Empty response without tool calls — shouldn't normally happen
            _logger.LogWarning("[Subagent:{Name}] Empty response at iteration {Iter}", Name, i + 1);
            return $"Subagent '{Name}' returned an empty response.";
        }

        // Max iterations reached — ask the LLM one final time using accumulated context
        _logger.LogWarning(
            "[Subagent:{Name}] MAX ITERATIONS ({Max}) reached | Total tokens so far: {InTok}+{OutTok}",
            Name, MaxIterations, totalInputTokens, totalOutputTokens);

        // Try to get one final summary without tools
        try
        {
            var finalResponse = await _claudeClient.SendAsync(
                SystemPrompt + "\n\nYou have reached the maximum number of steps. " +
                "Summarize your findings and produce the best plan you can with the information you have.",
                messages, tools: null, ct, maxTokens: 2048);

            totalInputTokens += finalResponse.Usage?.InputTokens ?? 0;
            totalOutputTokens += finalResponse.Usage?.OutputTokens ?? 0;

            var summary = finalResponse.GetText();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                totalStopwatch.Stop();
                _logger.LogInformation(
                    "[Subagent:{Name}] COMPLETED (forced summary) | Total iterations={Iter} | Total tokens: {InTok}+{OutTok} | Output={OutLen} chars | Total {TotalMs}ms\n" +
                    "── Subagent Output ──\n{Output}",
                    Name, MaxIterations, totalInputTokens, totalOutputTokens,
                    summary.Length, totalStopwatch.ElapsedMilliseconds,
                    Truncate(summary, 2000));
                return summary;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Subagent:{Name}] Final summary call FAILED after reaching max iterations", Name);
        }

        totalStopwatch.Stop();
        _logger.LogError(
            "[Subagent:{Name}] FAILED — no output after {Max} iterations and forced summary | Total {TotalMs}ms",
            Name, MaxIterations, totalStopwatch.ElapsedMilliseconds);
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

    // ── Logging helpers ────────────────────────────────────────

    /// <summary>Truncate a string for log display, adding "…" when cut.</summary>
    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }

    /// <summary>Build a compact human-readable summary of tool arguments.</summary>
    private static string SummarizeArgs(Dictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0) return "";
        var parts = new List<string>();
        foreach (var (k, v) in args)
        {
            var valStr = v switch
            {
                null => "null",
                string s when s.Length == 0 => "\"\"",
                string s when s.Length > 60 => $"\"{s[..60]}…\"",
                string s => $"\"{s}\"",
                _ => v.ToString() ?? "?"
            };
            parts.Add($"{k}={valStr}");
        }
        return string.Join(", ", parts);
    }
}
