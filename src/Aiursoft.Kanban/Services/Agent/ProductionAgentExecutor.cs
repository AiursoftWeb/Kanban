using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.Agent;

public class ProductionAgentExecutor(
    ToolRegistry toolRegistry,
    AdviceService adviceService,
    IAgentModelClient agentModelClient,
    TimeProvider timeProvider,
    ILogger<AgentService> logger)
{
    private readonly ToolRegistry _toolRegistry = toolRegistry;
    private readonly AdviceService _adviceService = adviceService;
    private readonly IAgentModelClient _agentModelClient = agentModelClient;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<AgentService> _logger = logger;

    private const int MaxLoops = 15;

    public async Task<AgentExecutionResult> ExecuteReActLoop(
        IServiceProvider sp,
        AgentConversation conversation,
        AgentExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var conversationId = conversation.Id;
        var toolTraces = new List<AgentToolTrace>();

        try
        {
            while (conversation.LoopCount < MaxLoops)
            {
                cancellationToken.ThrowIfCancellationRequested();
                conversation.LoopCount++;
                conversation.State = AgentState.Thinking;
                conversation.LastActivity = _timeProvider.GetUtcNow().UtcDateTime;

                var response = await CallLlmWithTools(conversation, cancellationToken);

                var toolUses = response.GetToolUses();
                if (toolUses.Count > 0)
                {
                    // Record assistant message with tool calls
                    var assistantToolCalls = toolUses.Select(tu => new ToolCallData
                    {
                        Id = tu.Id,
                        Type = "function",
                        Function = new ToolCallFunction
                        {
                            Name = tu.Name,
                            Arguments = JsonConvert.SerializeObject(UnwrapJsonElements(tu.Input ?? new()))
                        }
                    }).ToList();

                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = response.GetText(),
                        ToolCalls = assistantToolCalls,
                        ReasoningContent = response.ReasoningContent
                    });

                    var adviceIds = new List<Guid>();

                    foreach (var tu in toolUses)
                    {
                        if (string.IsNullOrEmpty(tu.Name)) continue;
                        var isWrite = _toolRegistry.IsWriteTool(tu.Name);

                        if (isWrite && !options.AutoApproveWrites)
                        {
                            var advice = await CreateAdvice(sp, conversationId, tu);
                            adviceIds.Add(advice.Id);
                            continue;
                        }

                        string toolResult;
                        try
                        {
                            toolResult = await ExecuteTool(sp, tu, conversation.UserId, cancellationToken);
                            _logger.LogInformation("{ToolKind} tool executed: {ToolName}",
                                isWrite ? "Write" : "Read",
                                tu.Name);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            toolResult = $"Error executing {tu.Name}: {ex.Message}";
                            _logger.LogWarning(ex, "{ToolKind} tool failed: {ToolName}",
                                isWrite ? "Write" : "Read",
                                tu.Name);
                        }

                        toolTraces.Add(new AgentToolTrace(
                            tu.Id ?? "",
                            tu.Name,
                            UnwrapJsonElements(tu.Input ?? new()),
                            toolResult,
                            conversation.LoopCount));
                        conversation.Messages.Add(new ToolMessagesItem
                        {
                            Role = "tool",
                            ToolCallId = tu.Id,
                            Content = toolResult
                        });
                    }

                    if (adviceIds.Count > 0)
                    {
                        conversation.PendingAdviceIds.AddRange(adviceIds);
                        conversation.State = AgentState.AwaitingApproval;
                        return new AgentExecutionResult(conversation, toolTraces);
                    }

                    continue;
                }

                // Text response, conversation complete
                var text = response.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = text,
                        ReasoningContent = response.ReasoningContent
                    });
                }
                else if (conversation.LoopCount == 1)
                {
                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = "I received an empty response from the model. Please check that the LLM endpoint is configured for the Anthropic Messages API format (/v1/messages)."
                    });
                }

                conversation.State = AgentState.Completed;
                return new AgentExecutionResult(conversation, toolTraces);
            }

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "assistant",
                Content = "I've reached the maximum number of steps. Please refine your request or approve pending actions."
            });
            conversation.State = AgentState.Completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent ReAct loop failed for conversation {ConversationId}", conversationId);
            conversation.State = AgentState.Error;
            conversation.ErrorMessage = ex.Message;
        }

        return new AgentExecutionResult(conversation, toolTraces);
    }

    private async Task<Advice> CreateAdvice(
        IServiceProvider sp,
        Guid conversationId,
        ClaudeContentBlock toolUse)
    {
        var tool = _toolRegistry.GetTool(toolUse.Name ?? "");
        var displayName = tool?.ProtocolTool.Title ?? toolUse.Name ?? "";
        var description = tool?.ProtocolTool.Description ?? "";
        var args = toolUse.Input ?? new Dictionary<string, object?>();
        var result = await BuildParameterDisplay(sp, toolUse.Name ?? "", args);
        var advice = _adviceService.Create(
            conversationId: conversationId,
            toolName: toolUse.Name ?? "",
            toolDisplayName: displayName,
            toolDescription: description,
            parameters: args,
            parameterDisplay: result.DisplayText,
            toolCallId: toolUse.Id,
            displayParameters: result.Parameters,
            resolvedName: result.ResolvedName);
        _logger.LogInformation("Advice created: {AdviceId} for tool {ToolName}", advice.Id, toolUse.Name);
        return advice;
    }

    public async Task ExecuteAdviceAndResume(
        IServiceProvider sp,
        AgentConversation conversation,
        Guid adviceId)
    {
        var conversationId = conversation.Id;

        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Approved) return;

        try
        {
            var tool = _toolRegistry.GetTool(advice.ToolName);
            if (tool == null)
            {
                _adviceService.SetResult(adviceId, null, $"Tool not found: {advice.ToolName}");
                return;
            }

            var args = new Dictionary<string, object?>(advice.Parameters);
            // Remove any stale userId that may have been stored in advice params
            args.Remove("userId");

            var result = await ExecuteToolWithArgs(sp, tool, args, conversation.UserId);

            _adviceService.SetResult(adviceId, result, null);

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = result
            });

            // Only resume the ReAct loop when ALL pending advice items are resolved.
            // If there are still pending items, stay in AwaitingApproval so the
            // user can approve/reject them. Otherwise the conversation history
            // would have an incomplete tool_calls → tool_result chain.
            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
            }
            else
            {
                conversation.State = AgentState.Thinking;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advice execution failed for {AdviceId}", adviceId);
            _adviceService.SetResult(adviceId, null, ex.Message);

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = $"Error executing tool: {ex.Message}"
            });

            // Same logic applies after errors: only resume when all pending items resolved
            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
                return;
            }
        }

        await ExecuteReActLoop(sp, conversation, new AgentExecutionOptions());
    }

    private async Task<ClaudeResponse> CallLlmWithTools(
        AgentConversation conversation,
        CancellationToken cancellationToken)
    {
        var systemPrompt = conversation.Messages
            .Where(m => m.Role == "system")
            .Select(m => m.Content)
            .FirstOrDefault() ?? "";

        var claudeMessages = ConvertToClaudeMessages(conversation.Messages);
        var tools = BuildClaudeTools();

        _logger.LogDebug("=== Agent request === System: {SystemPrompt}", TruncateDebug(systemPrompt));
        _logger.LogDebug("=== Agent request === Messages ({Count}): {Messages}",
            claudeMessages.Count,
            JsonConvert.SerializeObject(claudeMessages, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        _logger.LogDebug("=== Agent request === Tools ({Count}): {Tools}",
            tools.Count,
            JsonConvert.SerializeObject(tools.Select(t => new { t.Name, t.Description })));

        var response = await _agentModelClient.SendAsync(
            systemPrompt,
            claudeMessages,
            tools,
            cancellationToken);

        _logger.LogDebug("=== Agent response === Text: {Text}", TruncateDebug(response.GetText()));
        _logger.LogDebug("=== Agent response === ToolUses ({Count}): {Tools}",
            response.GetToolUses().Count,
            JsonConvert.SerializeObject(response.GetToolUses().Select(t => new { t.Name, t.Input })));
        _logger.LogDebug("=== Agent response === StopReason: {Reason}, Usage: {Usage}",
            response.StopReason,
            JsonConvert.SerializeObject(response.Usage));

        return response;
    }

    private static string TruncateDebug(string? value, int max = 2000)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= max ? value : value[..max] + $"... (truncated, total {value.Length} chars)";
    }

    private static List<ClaudeMessage> ConvertToClaudeMessages(List<ToolMessagesItem> messages)
    {
        var result = new List<ClaudeMessage>();

        foreach (var msg in messages.Where(m => m.Role != "system"))
        {
            if (msg.Role == "user")
            {
                result.Add(ClaudeMessage.User(msg.Content ?? ""));
            }
            else if (msg.Role == "assistant")
            {
                var blocks = new List<ClaudeContentBlock>();

                if (!string.IsNullOrWhiteSpace(msg.Content))
                    blocks.Add(ClaudeContentBlock.TextBlock(msg.Content));

                if (msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var input = TryParseArgs(tc.Function?.Arguments ?? "{}");
                        blocks.Add(ClaudeContentBlock.ToolUse(
                            tc.Id ?? Guid.NewGuid().ToString(),
                            tc.Function?.Name ?? "",
                            input));
                    }
                }

                result.Add(ClaudeMessage.Assistant(blocks, msg.ReasoningContent));
            }
            else if (msg.Role == "tool")
            {
                result.Add(ClaudeMessage.ToolResult(msg.ToolCallId ?? "", msg.Content ?? ""));
            }
        }

        return result;
    }

    private List<ClaudeTool> BuildClaudeTools()
    {
        // CurrentUserService is registered in DI, so MCP's AIFunctionFactory
        // automatically excludes it from InputSchema. No manual stripping needed.
        return _toolRegistry.AllTools.Select(tool =>
        {
            var proto = tool.ProtocolTool;
            return new ClaudeTool
            {
                Name = proto.Name,
                Description = proto.Description,
                InputSchema = System.Text.Json.JsonSerializer.Deserialize<object>(proto.InputSchema.GetRawText())!
            };
        }).ToList();
    }

    private async Task<string> ExecuteTool(
        IServiceProvider sp,
        ClaudeContentBlock toolUse,
        string userId,
        CancellationToken cancellationToken)
    {
        var tool = _toolRegistry.GetTool(toolUse.Name ?? "");
        if (tool == null) return $"Error: Unknown tool '{toolUse.Name}'.";

        var args = UnwrapJsonElements(toolUse.Input ?? new());
        // NEVER include userId in args — user identity is injected via CurrentUserService

        return await ExecuteToolWithArgs(sp, tool, args, userId, cancellationToken);
    }

    private async Task<string> ExecuteToolWithArgs(
        IServiceProvider sp,
        McpServerTool tool,
        Dictionary<string, object?> args,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = sp.CreateScope();

        // Set the current user on the scoped service before tool invocation.
        // The tool class gets CurrentUserService via constructor injection
        // (or as a method parameter excluded from schema by MCP SDK).
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = userId;

        var jsonArgs = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (key, value) in args)
        {
            // LLMs often send "" for optional parameters instead of omitting them
            // or sending null. Treat empty strings as null so nullable types (int?,
            // bool?, etc.) deserialize correctly.
            var sanitized = value is string s && s.Length == 0 ? null : value;
            var json = System.Text.Json.JsonSerializer.SerializeToElement(sanitized);
            jsonArgs[key] = json;
        }

        var requestParams = new ModelContextProtocol.Protocol.CallToolRequestParams
        {
            Name = tool.ProtocolTool.Name,
            Arguments = jsonArgs
        };

        var request = new RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams>(
            server: NullMcpServer.Instance,
            jsonRpcRequest: new ModelContextProtocol.Protocol.JsonRpcRequest { Method = "tools/call" },
            parameters: requestParams)
        {
            Services = scope.ServiceProvider
        };

        var result = await tool.InvokeAsync(request, cancellationToken);
        var textContent = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault();
        var resultText = textContent?.Text ?? result.ToString() ?? "Tool executed.";
        if (_toolRegistry.IsWriteTool(tool.ProtocolTool.Name) &&
            !resultText.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId);
            await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new AgentToolExecutedEvent(
                ToolName: tool.ProtocolTool.Name,
                UserId: userId,
                UserName: user?.DisplayName ?? user?.UserName ?? userId,
                Summary: resultText,
                Arguments: args), cancellationToken);
        }

        return resultText;
    }

    private static Dictionary<string, object?> UnwrapJsonElements(Dictionary<string, object?> args)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in args)
        {
            result[key] = value switch
            {
                System.Text.Json.JsonElement el => UnwrapJsonElement(el),
                _ => value
            };
        }
        return result;
    }

    private static object? UnwrapJsonElement(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => el.GetRawText()
        };
    }

    private static Dictionary<string, object?> TryParseArgs(string json)
    {
        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json) ?? new();
            return UnwrapJsonElements(dict);
        }
        catch
        {
            return new();
        }
    }

    private sealed record ParameterDisplayResult(
        string DisplayText,
        List<AdviceParameterItem> Parameters,
        string? ResolvedName);

    private static async Task<ParameterDisplayResult> BuildParameterDisplay(
        IServiceProvider sp,
        string toolName,
        Dictionary<string, object?> args)
    {
        var friendlyName = toolName switch
        {
            "CreateBoard" => "Create Board",
            "RenameBoard" => "Rename Board",
            "DeleteBoard" => "Delete Board",
            "CreateColumn" => "Create Column",
            "RenameColumn" => "Rename Column",
            "DeleteColumn" => "Delete Column",
            "UpdateColumnStatus" => "Update Column Status",
            "MoveColumn" => "Move Column",
            "CreateCard" => "Create Card",
            "MoveCard" => "Move Card",
            "UpdateCardDetails" => "Update Card Details",
            "AssignCard" => "Assign Card",
            "UpdateCardPriority" => "Update Card Priority",
            "AddLabel" => "Add Label",
            "RemoveLabel" => "Remove Label",
            "BatchCreateCards" => "Batch Create Cards",
            "BatchMoveCards" => "Batch Move Cards",
            "DeleteCard" => "Delete Card",
            _ => toolName
        };

        // Build structured parameter list for UI rendering
        var displayParams = new List<AdviceParameterItem>();
        foreach (var (key, value) in args)
        {
            if (key == "userId") continue;
            var displayKey = key switch
            {
                "columnId" => "Column",
                "targetColumnId" => "Target Column",
                "boardId" => "Board",
                "cardId" => "Card",
                "assignedUserId" => "Assignee",
                "plannedStartTime" => "Start",
                "dueDate" => "Due",
                "newOrder" => "Position",
                "labelId" => "Label",
                _ => key
            };
            displayParams.Add(new AdviceParameterItem
            {
                Key = key,
                DisplayKey = displayKey,
                Value = value?.ToString()
            });
        }

        // Batch-load entity names for better readability
        string? resolvedName = null;
        try
        {
            resolvedName = await ResolveDisplayName(sp, toolName, args);
        }
        catch
        {
            // Degrade gracefully — fall back to showing IDs
        }

        // Flat summary string (compact fallback)
        var flatParams = displayParams.Select(p => $"{p.DisplayKey}: {p.Value}");
        var displayText = $"{friendlyName}: {string.Join(", ", flatParams)}";
        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            displayText += $" | {resolvedName}";
        }

        return new ParameterDisplayResult(displayText, displayParams, resolvedName);
    }

    /// <summary>
    /// Looks up entity names for IDs used in tool arguments,
    /// producing a human-readable summary line (e.g. 'Card "Fix login" on Board "Sprint 1" → Column "Done"').
    /// Returns null when no names could be resolved.
    /// </summary>
    private static async Task<string?> ResolveDisplayName(IServiceProvider sp, string toolName, Dictionary<string, object?> args)
    {
        // Only resolve for tools where the context helps
        var relevantTools = new HashSet<string>
        {
            "MoveCard", "UpdateCardDetails", "AssignCard", "UpdateCardPriority",
            "DeleteCard", "AddLabel", "RemoveLabel",
            "MoveColumn", "RenameColumn", "DeleteColumn", "UpdateColumnStatus",
            "RenameBoard", "DeleteBoard",
            "BatchMoveCards"
        };
        if (!relevantTools.Contains(toolName))
            return null;

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var parts = new List<string>();

        // Resolve cardId
        if (args.TryGetValue("cardId", out var cardIdObj) && cardIdObj != null)
        {
            if (int.TryParse(cardIdObj.ToString(), out var cardId) && cardId > 0)
            {
                var card = await db.KanbanCards.FindAsync(cardId);
                if (card != null)
                {
                    parts.Add($"Card \"{card.Title}\"");
                }
            }
        }

        // Resolve cardIds (batch operations)
        var cardIds = new List<int>();
        if (args.TryGetValue("cardIds", out var cardIdsObj) && cardIdsObj != null)
        {
            if (cardIdsObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in je.EnumerateArray())
                {
                    if (el.TryGetInt32(out var cid))
                        cardIds.Add(cid);
                }
            }
        }
        if (cardIds.Count > 0)
        {
            var cards = await db.KanbanCards.Where(c => cardIds.Contains(c.Id)).ToListAsync();
            var titles = cards.Select(c => $"\"{c.Title}\"").ToList();
            if (titles.Count > 0)
                parts.Add($"Cards: {string.Join(", ", titles)}");
        }

        // Resolve columnId
        if (args.TryGetValue("columnId", out var colIdObj) && colIdObj != null)
        {
            if (int.TryParse(colIdObj.ToString(), out var colId) && colId > 0)
            {
                var col = await db.KanbanColumns.FindAsync(colId);
                if (col != null)
                {
                    parts.Add($"Column \"{col.Name}\"");
                }
            }
        }

        // Resolve targetColumnId
        if (args.TryGetValue("targetColumnId", out var tgtColIdObj) && tgtColIdObj != null)
        {
            if (int.TryParse(tgtColIdObj.ToString(), out var tgtColId) && tgtColId > 0)
            {
                var col = await db.KanbanColumns.FindAsync(tgtColId);
                if (col != null)
                {
                    parts.Add($"Target \"{col.Name}\"");
                }
            }
        }

        // Resolve boardId
        if (args.TryGetValue("boardId", out var boardIdObj) && boardIdObj != null)
        {
            if (int.TryParse(boardIdObj.ToString(), out var boardId) && boardId > 0)
            {
                var board = await db.KanbanBoards.FindAsync(boardId);
                if (board != null)
                {
                    parts.Add($"Board \"{board.Name}\"");
                }
            }
        }

        // Resolve labelId
        if (args.TryGetValue("labelId", out var labelIdObj) && labelIdObj != null)
        {
            if (int.TryParse(labelIdObj.ToString(), out var labelId) && labelId > 0)
            {
                var label = await db.KanbanLabels.FindAsync(labelId);
                if (label != null)
                {
                    parts.Add($"Label \"{label.Name}\"");
                }
            }
        }

        // Resolve assignedUserId → user name
        if (args.TryGetValue("assignedUserId", out var assigneeIdObj) && assigneeIdObj != null)
        {
            var assigneeId = assigneeIdObj.ToString();
            if (!string.IsNullOrWhiteSpace(assigneeId))
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                var user = await userManager.FindByIdAsync(assigneeId);
                if (user != null)
                {
                    var displayName = user.UserName ?? user.Email ?? assigneeId;
                    parts.Add($"Assignee \"{displayName}\"");
                }
            }
        }

        return parts.Count > 0 ? string.Join(" → ", parts) : null;
    }
}
