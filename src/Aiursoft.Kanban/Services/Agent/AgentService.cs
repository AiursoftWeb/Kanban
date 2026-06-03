using System.Collections.Concurrent;
using System.Text;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.Agent;

public class AgentService : IAgentService
{
    private readonly ConcurrentDictionary<Guid, AgentConversation> _conversations = new();
    private readonly ServiceTaskQueue _taskQueue;
    private readonly ToolRegistry _toolRegistry;
    private readonly AdviceService _adviceService;
    private readonly AgentPromptConfig _promptConfig;
    private readonly ClaudeClient _claudeClient;
    private readonly IServiceProvider _rootServices;
    private readonly ILogger<AgentService> _logger;

    private const int MaxLoops = 15;
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AdviceTtl = TimeSpan.FromMinutes(30);

    public AgentService(
        ServiceTaskQueue taskQueue,
        ToolRegistry toolRegistry,
        AdviceService adviceService,
        IOptions<AgentPromptConfig> promptConfig,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILogger<AgentService> logger)
    {
        _taskQueue = taskQueue;
        _toolRegistry = toolRegistry;
        _adviceService = adviceService;
        _promptConfig = promptConfig.Value;
        _claudeClient = claudeClient;
        _rootServices = rootServices;
        _logger = logger;
    }

    public Guid StartRun(string userId, int boardId, string userMessage)
    {
        CleanupExpiredConversations();

        var conversation = new AgentConversation
        {
            UserId = userId,
            BoardId = boardId,
        };

        // System prompt includes injected user context (name, roles, boards).
        // The context is NOT a user-visible message — it lives in the system prompt.
        var userContext = BuildUserContextBlock(userId, boardId);
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "system",
            Content = _promptConfig.SystemPrompt.Replace("{userContext}", userContext)
        });

        // Only the user's actual message is visible in the chat UI
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        _conversations[conversation.Id] = conversation;

        _taskQueue.QueueWithDependency<IServiceProvider>(
            queueName: "KanbanAgent",
            taskName: $"AgentRun-{conversation.Id}",
            task: async (sp) => await ExecuteReActLoop(sp, conversation.Id));

        return conversation.Id;
    }

    public Guid? ContinueRun(Guid conversationId, string userId, string userMessage)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
            return null;

        if (conversation.UserId != userId)
            return null;

        if (conversation.State is AgentState.Thinking or AgentState.AwaitingApproval)
            return null; // Already busy — caller should wait or cancel first

        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        conversation.State = AgentState.Thinking;
        conversation.LastActivity = DateTime.UtcNow;
        conversation.LoopCount = 0; // Reset loop counter for the new turn

        _taskQueue.QueueWithDependency<IServiceProvider>(
            queueName: "KanbanAgent",
            taskName: $"AgentContinue-{conversation.Id}",
            task: async (sp) => await ExecuteReActLoop(sp, conversation.Id));

        return conversation.Id;
    }

    public AgentConversation? GetConversation(Guid conversationId)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return conversation;
    }

    public void ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Pending) return;

        _adviceService.UpdateStatus(adviceId, AdviceStatus.Approved);

        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.PendingAdviceIds.Remove(adviceId);
            conversation.LastActivity = DateTime.UtcNow;

            _taskQueue.QueueWithDependency<IServiceProvider>(
                queueName: "KanbanAgent",
                taskName: $"AdviceExecute-{adviceId}",
                task: async (sp) => await ExecuteAdviceAndResume(sp, conversationId, adviceId));
        }
    }

    public void RejectAdvice(Guid conversationId, Guid adviceId)
    {
        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Pending) return;

        _adviceService.UpdateStatus(adviceId, AdviceStatus.Rejected);

        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.PendingAdviceIds.Remove(adviceId);
            conversation.LastActivity = DateTime.UtcNow;

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = $"REJECTED: User rejected this operation. Do not retry."
            });

            // Only resume when all pending advice items are resolved.
            // Otherwise the ReAct loop would send incomplete tool_calls → tool_result chain.
            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
            }
            else
            {
                _taskQueue.QueueWithDependency<IServiceProvider>(
                    queueName: "KanbanAgent",
                    taskName: $"ResumeAfterReject-{adviceId}",
                    task: async (sp) => await ExecuteReActLoop(sp, conversationId));
            }
        }
    }

    public void ApproveAll(Guid conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation)) return;

        var pendingIds = conversation.PendingAdviceIds.ToList();
        foreach (var adviceId in pendingIds)
        {
            ApproveAdvice(conversationId, adviceId);
        }
    }

    public void CancelRun(Guid conversationId)
    {
        if (_conversations.TryRemove(conversationId, out _))
        {
            _adviceService.RemoveConversationAdvice(conversationId);
        }
    }

    private async Task ExecuteReActLoop(IServiceProvider sp, Guid conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation)) return;

        try
        {
            while (conversation.LoopCount < MaxLoops)
            {
                conversation.LoopCount++;
                conversation.State = AgentState.Thinking;
                conversation.LastActivity = DateTime.UtcNow;

                var response = await CallLlmWithTools(conversation);

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

                        if (isWrite)
                        {
                            var tool = _toolRegistry.GetTool(tu.Name);
                            var displayName = tool?.ProtocolTool.Title ?? tu.Name;
                            var description = tool?.ProtocolTool.Description ?? "";

                            var args = tu.Input ?? new Dictionary<string, object?>();
                            var paramDisplay = BuildParameterDisplay(tu.Name, args);

                            var advice = _adviceService.Create(
                                conversationId: conversationId,
                                toolName: tu.Name,
                                toolDisplayName: displayName,
                                toolDescription: description,
                                parameters: args,
                                parameterDisplay: paramDisplay,
                                toolCallId: tu.Id);

                            adviceIds.Add(advice.Id);
                            _logger.LogInformation("Advice created: {AdviceId} for tool {ToolName}", advice.Id, tu.Name);
                        }
                        else
                        {
                            string result;
                            try
                            {
                                result = await ExecuteTool(sp, tu, conversation.UserId);
                                _logger.LogInformation("Read tool executed: {ToolName}", tu.Name);
                            }
                            catch (Exception ex)
                            {
                                result = $"Error executing {tu.Name}: {ex.Message}";
                                _logger.LogWarning(ex, "Read tool failed: {ToolName}", tu.Name);
                            }
                            conversation.Messages.Add(new ToolMessagesItem
                            {
                                Role = "tool",
                                ToolCallId = tu.Id,
                                Content = result
                            });
                        }
                    }

                    if (adviceIds.Count > 0)
                    {
                        conversation.PendingAdviceIds.AddRange(adviceIds);
                        conversation.State = AgentState.AwaitingApproval;
                        return;
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
                return;
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
    }

    private async Task ExecuteAdviceAndResume(IServiceProvider sp, Guid conversationId, Guid adviceId)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation)) return;

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

        await ExecuteReActLoop(sp, conversationId);
    }

    private async Task<ClaudeResponse> CallLlmWithTools(AgentConversation conversation)
    {
        var systemPrompt = conversation.Messages
            .Where(m => m.Role == "system")
            .Select(m => m.Content)
            .FirstOrDefault() ?? "";

        var claudeMessages = ConvertToClaudeMessages(conversation.Messages);
        var tools = BuildClaudeTools();

        return await _claudeClient.SendAsync(systemPrompt, claudeMessages, tools);
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

    /// <summary>
    /// Builds a context block injected into the system prompt via {userContext}.
    /// This information is NOT visible to the user in the chat UI — it lives
    /// in the system prompt so the LLM has the facts it needs without cluttering
    /// the conversation.
    /// </summary>
    private string BuildUserContextBlock(string userId, int boardId)
    {
        using var scope = _rootServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var user = userManager.FindByIdAsync(userId).GetAwaiter().GetResult();
        var userName = user?.DisplayName ?? user?.UserName ?? user?.Email ?? userId;

        var roles = user != null
            ? userManager.GetRolesAsync(user).GetAwaiter().GetResult()
            : [];

        var ownedBoards = db.KanbanBoards
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Order)
            .Select(b => b.Name)
            .ToList();

        var currentBoardName = db.KanbanBoards
            .Where(b => b.Id == boardId)
            .Select(b => b.Name)
            .FirstOrDefault();

        var sb = new StringBuilder();
        sb.Append("Current user: ").AppendLine(userName);
        sb.Append("Your roles: ").AppendLine(roles.Count > 0 ? string.Join(", ", roles) : "(none)");
        sb.Append("Boards you own: ");
        if (ownedBoards.Count > 0)
        {
            sb.Append(ownedBoards.Count).Append(" total. ");
            sb.AppendLine(string.Join(", ", ownedBoards));
        }
        else
        {
            sb.AppendLine("(none)");
        }
        sb.Append("Current board: ");
        sb.Append(currentBoardName ?? "(unnamed)");
        sb.Append(" (ID: ").Append(boardId).AppendLine(").");
        sb.AppendLine("All operations are performed as this user. The server handles identity automatically.");

        return sb.ToString();
    }

    /// <summary>
    /// Removes conversations and their advice that have been inactive longer than the TTL.
    /// Called on each new conversation start — lazy cleanup with zero overhead when idle.
    /// </summary>
    private void CleanupExpiredConversations()
    {
        var conversationCutoff = DateTime.UtcNow - ConversationTtl;
        var adviceCutoff = DateTime.UtcNow - AdviceTtl;

        foreach (var (id, conv) in _conversations)
        {
            if (conv.LastActivity < conversationCutoff && _conversations.TryRemove(id, out _))
                _adviceService.RemoveConversationAdvice(id);
        }

        // Also sweep orphaned advice (from conversations removed by CancelRun)
        _adviceService.RemoveExpiredAdvice(adviceCutoff);
    }

    private async Task<string> ExecuteTool(IServiceProvider sp, ClaudeContentBlock toolUse, string userId)
    {
        var tool = _toolRegistry.GetTool(toolUse.Name ?? "");
        if (tool == null) return $"Error: Unknown tool '{toolUse.Name}'.";

        var args = UnwrapJsonElements(toolUse.Input ?? new());
        // NEVER include userId in args — user identity is injected via CurrentUserService

        return await ExecuteToolWithArgs(sp, tool, args, userId);
    }

    private async Task<string> ExecuteToolWithArgs(IServiceProvider sp, McpServerTool tool, Dictionary<string, object?> args, string userId)
    {
        using var scope = sp.CreateScope();

        // Set the current user on the scoped service before tool invocation.
        // The tool class gets CurrentUserService via constructor injection
        // (or as a method parameter excluded from schema by MCP SDK).
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = userId;

        var jsonArgs = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (key, value) in args)
        {
            var json = System.Text.Json.JsonSerializer.SerializeToElement(value);
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

        var result = await tool.InvokeAsync(request);
        var textContent = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault();
        return textContent?.Text ?? result.ToString() ?? "Tool executed.";
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

    private static string BuildParameterDisplay(string toolName, Dictionary<string, object?> args)
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
            "UpdateLabelColor" => "Update Label Color",
            "BatchCreateCards" => "Batch Create Cards",
            "BatchMoveCards" => "Batch Move Cards",
            _ => toolName
        };

        var details = new List<string>();
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
            details.Add($"{displayKey}: {value}");
        }

        return $"{friendlyName}: {string.Join(", ", details)}";
    }
}
