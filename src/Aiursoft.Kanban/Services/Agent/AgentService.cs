using System.Collections.Concurrent;
using System.Text;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    private const string SystemReminder =
        "<system-reminder>\n" +
        "This conversation is about a Kanban board application. Please process user messages using the following steps:\n" +
        "1. Think step by step — plan and generate a strategy before acting.\n" +
        "2. Extract entities from the user message (e.g., task name, assignee, time, status, priority).\n" +
        "3. Use search tools to look up relevant existing cards and boards.\n" +
        "4. Perform intent recognition. For example:\n" +
        "   - User says \"completed the code\" or \"finished XX\" \"XX has been configured\", \"XX has been completed.\" → likely intent: move card \"XX\" to the \"Done\" column.\n" +
        "   - User says \"add XX\" or \"need XX\" → likely intent: create a new card.\n" +
        "   - User says \"how is XX going\" → likely intent: check the status of related cards.\n" +
        "   - User expresses a seemingly unrelated need (e.g., \"I want a hamburger\") → likely intent: create a task related to team dining or social events.\n" +
        "   - Users may mention the Kanban project itself in the Kanban board(e.g., \"The Kanban apikey has been configured\") → likely intent: Move related card to done column.\n" +
        "   - ALWAYS interpret messages as Kanban board operations. Only handle Kanban card-related tasks and questions. All seemingly unrelated messages are expressions of Kanban board actions.\n" +
        "5. Can infer the user’s actual intentions and  ask  user if it is right, but do not invoke write tools until you fully understand the context and the user’s intent.\n" +
        "6. For specific operations, do not directly ask how to operate. Instead, you should first search for the most relevant boards or cards and speculate on the most likely execution path. Only ask if the user refuses.\nIf the user gives a task, you need to put it in the most relevant Kanban board. For example: configuring the Kanban API key should be placed in the Kanban development task board.\n" +
        "{currentDateTime}\n" +
        "</system-reminder>";

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
            Content = _promptConfig.SystemPrompt
                .Replace("{userContext}", userContext)
                .Replace("{currentDateTime}", GetCurrentDateTimeBlock())
        });

        // Only the user's actual message is visible in the chat UI
        var reminder = SystemReminder.Replace("{currentDateTime}", GetCurrentDateTimeBlock());
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = reminder,
            IsMeta = true
        });
        var recentCards = BuildRecentCardsBlock(userId);
        if (!string.IsNullOrEmpty(recentCards))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = recentCards,
                IsMeta = true
            });
        }
        var assignedCards = BuildAssignedCardsBlock(userId);
        if (!string.IsNullOrEmpty(assignedCards))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = assignedCards,
                IsMeta = true
            });
        }
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

        var reminder = SystemReminder.Replace("{currentDateTime}", GetCurrentDateTimeBlock());
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = reminder,
            IsMeta = true
        });
        var recentCards = BuildRecentCardsBlock(userId);
        if (!string.IsNullOrEmpty(recentCards))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = recentCards,
                IsMeta = true
            });
        }
        var assignedCards = BuildAssignedCardsBlock(userId);
        if (!string.IsNullOrEmpty(assignedCards))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = assignedCards,
                IsMeta = true
            });
        }
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
                            var result = await BuildParameterDisplay(sp, tu.Name, args);

                            var advice = _adviceService.Create(
                                conversationId: conversationId,
                                toolName: tu.Name,
                                toolDisplayName: displayName,
                                toolDescription: description,
                                parameters: args,
                                parameterDisplay: result.DisplayText,
                                toolCallId: tu.Id,
                                displayParameters: result.Parameters,
                                resolvedName: result.ResolvedName);

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
    /// Builds a string like "Current time: Wednesday, June 10, 2026, 03:45 PM UTC"
    /// injected into the system-reminder via {currentDateTime}.
    /// </summary>
    private static string GetCurrentDateTimeBlock()
    {
        var now = DateTime.UtcNow;
        return $"Current time: {now:dddd, MMMM d, yyyy, h:mm tt} UTC";
    }

    /// <summary>
    /// Builds a system-reminder block listing the user's 10 most recently created
    /// cards with their board context, so the agent has awareness of recent activity.
    /// Card titles over 200 characters are truncated.
    /// </summary>
    private string BuildRecentCardsBlock(string userId)
    {
        using var scope = _rootServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var recentCards = db.KanbanCards
            .Include(c => c.Column)
            .ThenInclude(col => col.Board)
            .Where(c => c.Column.Board.UserId == userId || c.AssignedUserId == userId)
            .OrderByDescending(c => c.CreationTime)
            .Take(10)
            .Select(c => new
            {
                c.Title,
                ColumnName = c.Column.Name,
                BoardName = c.Column.Board.Name
            })
            .ToList();

        if (recentCards.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<system-reminder>");
        sb.AppendLine("Recently active cards (newest first):");
        foreach (var card in recentCards)
        {
            var title = card.Title.Length > 200
                ? card.Title[..200] + "..."
                : card.Title;
            sb.Append("- \"").Append(title).Append("\"");
            sb.Append(" (Board: \"").Append(card.BoardName).Append('"');
            sb.Append(", Column: \"").Append(card.ColumnName).Append("\")");
            sb.AppendLine();
        }
        sb.Append("│  IMPORTANT: this context may or may not be relevant    │\n</system-reminder>");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a system-reminder block listing up to 10 cards assigned to the user,
    /// sorted by priority (urgent first) then due date (earliest first).
    /// Card titles over 200 characters are truncated.
    /// </summary>
    private string BuildAssignedCardsBlock(string userId)
    {
        using var scope = _rootServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var assignedCards = db.KanbanCards
            .Include(c => c.Column)
            .ThenInclude(col => col.Board)
            .Where(c => c.AssignedUserId == userId)
            .OrderBy(c => c.Priority)                         // Urgent(0) first, None(4) last
            .ThenBy(c => c.DueDate ?? DateTime.MaxValue)      // earliest due date first, nulls last
            .ThenByDescending(c => c.CreationTime)            // newest first within same bucket
            .Take(10)
            .Select(c => new
            {
                c.Title,
                c.Priority,
                c.DueDate,
                ColumnName = c.Column.Name,
                BoardName = c.Column.Board.Name
            })
            .ToList();

        if (assignedCards.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<system-reminder>");
        sb.AppendLine("Cards assigned to you (priority order):");
        foreach (var card in assignedCards)
        {
            var title = card.Title.Length > 200
                ? card.Title[..200] + "..."
                : card.Title;
            sb.Append("- \"").Append(title).Append("\"");
            sb.Append(" [").Append(card.Priority).Append(']');
            if (card.DueDate.HasValue)
            {
                sb.Append(" Due: ").Append(card.DueDate.Value.ToString("yyyy-MM-dd"));
            }
            sb.Append(" (Board: \"").Append(card.BoardName).Append('"');
            sb.Append(", Column: \"").Append(card.ColumnName).Append("\")");
            sb.AppendLine();
        }
        sb.Append("│  IMPORTANT: this context may or may not be relevant    │\n</system-reminder>");

        return sb.ToString();
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

        if (boardId > 0)
        {
            var currentBoardName = db.KanbanBoards
                .Where(b => b.Id == boardId)
                .Select(b => b.Name)
                .FirstOrDefault();
            sb.Append("Current board: ");
            sb.Append(currentBoardName ?? "(unnamed)");
            sb.Append(" (ID: ").Append(boardId).AppendLine(").");
        }

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
            "UpdateLabelColor" => "Update Label Color",
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
            "DeleteCard", "AddLabel", "RemoveLabel", "UpdateLabelColor",
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
