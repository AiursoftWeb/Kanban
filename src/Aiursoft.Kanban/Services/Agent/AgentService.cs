using System.Collections.Concurrent;
using System.Text;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.GptClient.Abstractions;
using Aiursoft.GptClient.Services;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Aiursoft.Kanban.Services.Agent;

public class AgentService : IAgentService
{
    private readonly ConcurrentDictionary<Guid, AgentConversation> _conversations = new();
    private readonly ServiceTaskQueue _taskQueue;
    private readonly ToolRegistry _toolRegistry;
    private readonly AdviceService _adviceService;
    private readonly OpenAIConfiguration _config;
    private readonly ChatClient _chatClient;
    private readonly IServiceProvider _rootServices;
    private readonly ILogger<AgentService> _logger;

    private const int MaxLoops = 15;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new DefaultContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    public AgentService(
        ServiceTaskQueue taskQueue,
        ToolRegistry toolRegistry,
        AdviceService adviceService,
        IOptions<OpenAIConfiguration> config,
        ChatClient chatClient,
        IServiceProvider rootServices,
        ILogger<AgentService> logger)
    {
        _taskQueue = taskQueue;
        _toolRegistry = toolRegistry;
        _adviceService = adviceService;
        _config = config.Value;
        _chatClient = chatClient;
        _rootServices = rootServices;
        _logger = logger;
    }

    public Guid StartRun(string userId, int boardId, string userMessage)
    {
        var conversation = new AgentConversation
        {
            UserId = userId,
            BoardId = boardId,
        };

        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "system",
            Content = BuildSystemPrompt(boardId, userId)
        });

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

            _taskQueue.QueueWithDependency<IServiceProvider>(
                queueName: "KanbanAgent",
                taskName: $"ResumeAfterReject-{adviceId}",
                task: async (sp) => await ExecuteReActLoop(sp, conversationId));
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
        if (_conversations.TryRemove(conversationId, out var conversation))
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

                var response = await CallLlmWithTools(conversation.Messages);

                if (response.ToolCalls != null && response.ToolCalls.Count > 0)
                {
                    // Add assistant message with tool calls
                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = response.Content ?? "",
                        ToolCalls = response.ToolCalls
                    });

                    var adviceIds = new List<Guid>();

                    foreach (var toolCall in response.ToolCalls)
                    {
                        if (toolCall.Function == null) continue;
                        var isWrite = _toolRegistry.IsWriteTool(toolCall.Function.Name!);

                        if (isWrite)
                        {
                            var tool = _toolRegistry.GetTool(toolCall.Function.Name!);
                            var displayName = tool?.ProtocolTool.Title ?? toolCall.Function.Name!;
                            var description = tool?.ProtocolTool.Description ?? "";

                            var args = TryParseArgs(toolCall.Function.Arguments ?? "{}");
                            var paramDisplay = BuildParameterDisplay(toolCall.Function.Name!, args);

                            var advice = _adviceService.Create(
                                conversationId: conversationId,
                                toolName: toolCall.Function.Name!,
                                toolDisplayName: displayName,
                                toolDescription: description ?? "",
                                parameters: args,
                                parameterDisplay: paramDisplay,
                                toolCallId: toolCall.Id);

                            adviceIds.Add(advice.Id);
                            _logger.LogInformation("Advice created: {AdviceId} for tool {ToolName}", advice.Id, toolCall.Function.Name);
                        }
                        else
                        {
                            // Execute read tool immediately
                            var result = await ExecuteTool(sp, toolCall, conversation.UserId);
                            conversation.Messages.Add(new ToolMessagesItem
                            {
                                Role = "tool",
                                ToolCallId = toolCall.Id,
                                Content = result
                            });
                            _logger.LogInformation("Read tool executed: {ToolName}", toolCall.Function.Name);
                        }
                    }

                    if (adviceIds.Count > 0)
                    {
                        conversation.PendingAdviceIds.AddRange(adviceIds);
                        conversation.State = AgentState.AwaitingApproval;
                        return; // Pause for user approval
                    }

                    // All read tools executed, continue loop
                    continue;
                }

                // Text response, conversation complete
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = response.Content
                    });
                }

                conversation.State = AgentState.Completed;
                return;
            }

            // Max loops exceeded
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

            // Build arguments from advice parameters
            var args = new Dictionary<string, object?>(advice.Parameters);
            if (!args.ContainsKey("userId"))
                args["userId"] = conversation.UserId;

            // Execute tool via MCP SDK
            var result = await ExecuteToolWithArgs(sp, tool, args);

            _adviceService.SetResult(adviceId, result, null);

            // Add tool result to conversation
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = result
            });

            conversation.State = AgentState.Thinking;
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
        }

        // Resume ReAct loop
        await ExecuteReActLoop(sp, conversationId);
    }

    private async Task<LlmResponse> CallLlmWithTools(List<ToolMessagesItem> messages)
    {
        if (string.IsNullOrWhiteSpace(_config.CompletionApiUrl))
            throw new InvalidOperationException("OpenAI/Ollama CompletionApiUrl is not configured. Please set AppSettings:OpenAI:CompletionApiUrl in appsettings.json.");

        var toolsList = BuildToolsList();

        var requestModel = new OllamaRequestModel
        {
            Model = _config.Model,
            Messages = messages.Cast<MessagesItem>().ToList(),
            Tools = toolsList,
            Stream = false,
            Temperature = 0.3
        };

        var json = JsonConvert.SerializeObject(requestModel, JsonSettings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        if (!string.IsNullOrWhiteSpace(_config.Token))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Token);
        }

        var response = await httpClient.PostAsync(_config.CompletionApiUrl, content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        return ParseLlmResponse(responseBody);
    }

    private static LlmResponse ParseLlmResponse(string responseBody)
    {
        var doc = JsonConvert.DeserializeObject<dynamic>(responseBody);
        if (doc == null) return new LlmResponse();

        var message = doc.message;
        string? textContent = message?.content?.ToString();

        var toolCalls = new List<ToolCallData>();
        var toolCallsArray = message?.tool_calls;
        if (toolCallsArray != null)
        {
            foreach (var tc in toolCallsArray)
            {
                toolCalls.Add(new ToolCallData
                {
                    Id = tc.id?.ToString() ?? Guid.NewGuid().ToString(),
                    Type = tc.type?.ToString() ?? "function",
                    Function = new ToolCallFunction
                    {
                        Name = tc.function?.name?.ToString(),
                        Arguments = tc.function?.arguments?.ToString()
                    }
                });
            }
        }

        // Fallback: parse <function_calls> XML from text content (for models that don't support native tool calling)
        if (toolCalls.Count == 0 && !string.IsNullOrWhiteSpace(textContent) && textContent.Contains("<function_calls>"))
        {
            var parsedCalls = ParseXmlFunctionCalls(textContent);
            if (parsedCalls.Count > 0)
            {
                toolCalls = parsedCalls;
                // Strip the XML block from displayed content
                var xmlStart = textContent.IndexOf("<function_calls>");
                var xmlEnd = textContent.IndexOf("</function_calls>") + "</function_calls>".Length;
                if (xmlStart >= 0 && xmlEnd > xmlStart)
                    textContent = textContent.Remove(xmlStart, xmlEnd - xmlStart).Trim();
            }
        }

        return new LlmResponse
        {
            Content = textContent,
            ToolCalls = toolCalls
        };
    }

    private static List<ToolCallData> ParseXmlFunctionCalls(string text)
    {
        var result = new List<ToolCallData>();
        var fcStart = text.IndexOf("<function_calls>");
        var fcEnd = text.IndexOf("</function_calls>");
        if (fcStart < 0 || fcEnd < 0) return result;

        var block = text.Substring(fcStart, fcEnd - fcStart + "</function_calls>".Length);
        var invokeRegex = new System.Text.RegularExpressions.Regex(
            @"<invoke\s+name=""([^""]+)""\s*>(.*?)</invoke>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in invokeRegex.Matches(block))
        {
            var name = match.Groups[1].Value;
            var inner = match.Groups[2].Value;

            var args = new Dictionary<string, object?>();
            var paramRegex = new System.Text.RegularExpressions.Regex(
                @"<parameter\s+name=""([^""]+)""\s*>(.*?)</parameter>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match pm in paramRegex.Matches(inner))
            {
                var paramName = pm.Groups[1].Value;
                var paramValue = pm.Groups[2].Value.Trim();
                args[paramName] = CoerceValue(paramValue);
            }

            result.Add(new ToolCallData
            {
                Id = Guid.NewGuid().ToString(),
                Type = "function",
                Function = new ToolCallFunction
                {
                    Name = name,
                    Arguments = JsonConvert.SerializeObject(args)
                }
            });
        }

        return result;
    }

    private static object? CoerceValue(string value)
    {
        if (int.TryParse(value, out var i)) return i;
        if (long.TryParse(value, out var l)) return l;
        if (bool.TryParse(value, out var b)) return b;
        return value;
    }

    private List<ToolsItem> BuildToolsList()
    {
        var result = new List<ToolsItem>();
        foreach (var tool in _toolRegistry.AllTools)
        {
            var proto = tool.ProtocolTool;
            var schema = NormalizeJsonSchema(proto.InputSchema);
            var parameters = JsonConvert.DeserializeObject<ParametersDefinition>(schema);

            result.Add(new ToolsItem
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = proto.Name,
                    Description = proto.Description,
                    Parameters = parameters
                }
            });
        }
        return result;
    }

    private static string NormalizeJsonSchema(System.Text.Json.JsonElement schema)
    {
        var obj = JObject.Parse(schema.GetRawText());
        if (obj["properties"] is JObject properties)
        {
            foreach (var prop in properties.Properties())
            {
                if (prop.Value is JObject propObj && propObj["type"] is JArray typeArray)
                {
                    var nonNull = typeArray.FirstOrDefault(t => t.Value<string>() != "null");
                    propObj["type"] = nonNull?.Value<string>() ?? "string";
                }
            }
        }
        return obj.ToString();
    }

    private string BuildSystemPrompt(int boardId, string userId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a Kanban board assistant. You help project managers manage their Kanban boards.");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("1. Use the available tools to read or modify the board.");
        sb.AppendLine("2. For operations that change data (create, update, delete, move), the system will ask the user to approve before executing.");
        sb.AppendLine("3. When you need more information, ask the user clarifying questions before calling tools.");
        sb.AppendLine("4. Be precise. When searching for users by name, use SearchUsers first to find the correct user ID.");
        sb.AppendLine("5. If a card doesn't exist, create it. If it already exists, move or update it. Always check first.");
        sb.AppendLine("6. The user may paste unstructured data. Parse it carefully and ask for clarification if ambiguous.");
        sb.AppendLine($"7. The current board ID is {boardId}. The current user ID is \"{userId}\".");
        sb.AppendLine();
        sb.AppendLine("## How to Call Tools");
        sb.AppendLine("To call tools, use this EXACT format in your response:");
        sb.AppendLine("<function_calls>");
        sb.AppendLine("<invoke name=\"ToolName\">");
        sb.AppendLine("<parameter name=\"paramName\">value</parameter>");
        sb.AppendLine("</invoke>");
        sb.AppendLine("</function_calls>");
        sb.AppendLine("You can include multiple <invoke> blocks to call multiple tools at once.");
        sb.AppendLine("The userId parameter is always the current user ID shown above.");
        sb.AppendLine("Example:");
        sb.AppendLine("<function_calls>");
        sb.AppendLine($"<invoke name=\"GetBoardById\"><parameter name=\"boardId\">{boardId}</parameter><parameter name=\"userId\">{userId}</parameter></invoke>");
        sb.AppendLine("</function_calls>");
        sb.AppendLine();
        sb.AppendLine("## Available Tools");
        sb.AppendLine("- Read tools: GetBoardById, GetColumns, GetCardsInColumn, GetCardById, SearchCards, GetOverdueCards, GetCardsByPriority, GetUnassignedCards, GetCardsByLabel, GetUserBoards, SearchBoards, GetBoardMembers, SearchUsers, SearchLabels, GetLabelsForCard, GetColumnById");
        sb.AppendLine("- Write tools (require approval): CreateBoard, RenameBoard, DeleteBoard, CreateColumn, RenameColumn, DeleteColumn, UpdateColumnStatus, MoveColumn, CreateCard, MoveCard, UpdateCardDetails, AssignCard, UpdateCardPriority, AddLabel, RemoveLabel, UpdateLabelColor, BatchCreateCards, BatchMoveCards");
        sb.AppendLine();
        sb.AppendLine("Always use the tools to interact with the board. Do not guess IDs or names - use SearchCards, SearchUsers, GetColumns, etc. to look them up first.");
        return sb.ToString();
    }

    private async Task<string> ExecuteTool(IServiceProvider sp, ToolCallData toolCall, string userId)
    {
        var tool = _toolRegistry.GetTool(toolCall.Function?.Name ?? "");
        if (tool == null) return $"Error: Unknown tool '{toolCall.Function?.Name}'.";

        var args = TryParseArgs(toolCall.Function?.Arguments ?? "{}");
        if (!args.ContainsKey("userId"))
            args["userId"] = userId;

        return await ExecuteToolWithArgs(sp, tool, args);
    }

    private async Task<string> ExecuteToolWithArgs(IServiceProvider sp, McpServerTool tool, Dictionary<string, object?> args)
    {
        using var scope = sp.CreateScope();
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

        var request = new ModelContextProtocol.Server.RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams>(
            server: null!,
            jsonRpcRequest: new ModelContextProtocol.Protocol.JsonRpcRequest { Method = "tools/call" },
            parameters: requestParams)
        {
            Services = scope.ServiceProvider
        };

        var result = await tool.InvokeAsync(request);
        var textContent = result.Content?.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault();
        return textContent?.Text ?? result.ToString() ?? "Tool executed.";
    }

    private static Dictionary<string, object?> TryParseArgs(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<string, object?>>(json) ?? new();
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

    private class LlmResponse
    {
        public string? Content { get; set; }
        public List<ToolCallData> ToolCalls { get; set; } = [];
    }
}
