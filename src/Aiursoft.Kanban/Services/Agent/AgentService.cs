using System.Collections.Concurrent;
using System.Text;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.Agent;

public class AgentService : IAgentService
{
    private readonly ConcurrentDictionary<Guid, AgentConversation> _conversations = new();
    private readonly ServiceTaskQueue _taskQueue;
    private readonly AdviceService _adviceService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProductionAgentExecutor _productionAgentExecutor;
    private readonly TimeProvider _timeProvider;

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
        "</system-reminder>";

    private const string ExcelSystemReminder =
        "<system-reminder>\n" +
        "The user has attached an Excel spreadsheet converted to markdown format below. When processing this table:\n" +
        "- Accurately identify rows and columns from the markdown table structure.\n" +
        "- Correctly determine which row contains the header/column names -- look for the separator row (|---|---|) to identify headers.\n" +
        "- Note that \"Unnamed: N\" column headers mean the original Excel had no header in that column -- infer the meaning from the cell content if possible, or treat row 1 as data rather than header.\n" +
        "- Be aware that the spreadsheet may contain merged cells, which can cause irregular column counts or missing cell values. Handle these carefully.\n" +
        "- Merged cells in the original Excel may cause missing values (NaN or empty cells) -- do not treat these as errors, they inherit the value from the nearest non-empty cell above or to the left.\n" +
        "- Pay special attention to multi-level or nested headers that may span multiple rows.\n" +
        "- If the table structure is ambiguous, ask the user for clarification before making assumptions.\n" +
        "- Process and respond to the user's request based on this table data.\n" +
        "</system-reminder>";

    public AgentService(
        ServiceTaskQueue taskQueue,
        AdviceService adviceService,
        IServiceScopeFactory scopeFactory,
        ProductionAgentExecutor productionAgentExecutor,
        TimeProvider timeProvider)
    {
        _taskQueue = taskQueue;
        _adviceService = adviceService;
        _scopeFactory = scopeFactory;
        _productionAgentExecutor = productionAgentExecutor;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> StartRun(string userId, int boardId, string userMessage, string? excelMarkdown = null)
    {
        CleanupExpiredConversations();

        var conversation = await CreateConversation(userId, boardId, userMessage, excelMarkdown);
        _conversations[conversation.Id] = conversation;

        _taskQueue.QueueWithDependency<IServiceProvider>(
            queueName: "KanbanAgent",
            taskName: $"AgentRun-{conversation.Id}",
            task: async (sp) => await ExecuteReActLoop(sp, conversation.Id));

        return conversation.Id;
    }

    private async Task<AgentConversation> CreateConversation(
        string userId,
        int boardId,
        string userMessage,
        string? excelMarkdown = null)
    {
        var conversation = new AgentConversation
        {
            UserId = userId,
            BoardId = boardId,
        };

        // System prompt includes injected user context (name, roles, boards).
        // The context is NOT a user-visible message — it lives in the system prompt.
        var userContext = BuildUserContextBlock(userId, boardId);
        using var settingsScope = _scopeFactory.CreateScope();
        var globalSettings = settingsScope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var systemPrompt = await globalSettings.GetSettingValueAsync(SettingsMap.AgentSystemPrompt);
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "system",
            Content = systemPrompt
                .Replace("{userContext}", userContext)
                .Replace("{currentDateTime}", GetCurrentDateTimeBlock())
        });

        // Only the user's actual message is visible in the chat UI
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = SystemReminder,
            IsMeta = true
        });
        var recentCards = BuildRecentCardsBlock(userId, count: 10);
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
        var unreadNotifications = BuildUnreadNotificationsBlock(userId);
        if (!string.IsNullOrEmpty(unreadNotifications))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = unreadNotifications,
                IsMeta = true
            });
        }
        var weeklyGuidance = BuildWeeklyGuidanceBlock(userMessage);
        if (!string.IsNullOrEmpty(weeklyGuidance))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = weeklyGuidance,
                IsMeta = true
            });
        }
        var taskPlanningGuidance = BuildTaskPlanningGuidanceBlock(userMessage);
        if (!string.IsNullOrEmpty(taskPlanningGuidance))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = taskPlanningGuidance,
                IsMeta = true
            });
        }
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        if (!string.IsNullOrWhiteSpace(excelMarkdown))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = ExcelSystemReminder,
                IsMeta = true
            });
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = excelMarkdown,
                IsMeta = true
            });
        }

        conversation.LastActivity = _timeProvider.GetUtcNow().UtcDateTime;
        return conversation;
    }

    public async Task<AgentExecutionResult> RunDirectAsync(
        string userId,
        int boardId,
        string userMessage,
        AgentExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var conversation = await CreateConversation(userId, boardId, userMessage);
        using var executionScope = _scopeFactory.CreateScope();
        return await _productionAgentExecutor.ExecuteReActLoop(
            executionScope.ServiceProvider,
            conversation,
            options,
            cancellationToken);
    }

    public Guid? ContinueRun(Guid conversationId, string userId, string userMessage, string? excelMarkdown = null)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
            return null;

        if (conversation.UserId != userId)
            return null;

        if (conversation.State is AgentState.Thinking or AgentState.AwaitingApproval)
            return null; // Already busy — caller should wait or cancel first

        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        // SystemReminder and assigned cards are injected once in StartRun.
        // Each turn: inject current time + 3 most recent cards (keeps agent
        // aware of real-time changes) and the conditional WeeklyGuidance.
        var recentCards = BuildRecentCardsBlock(userId, count: 3);
        if (!string.IsNullOrEmpty(recentCards))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = recentCards,
                IsMeta = true
            });
        }
        var unreadNotifications = BuildUnreadNotificationsBlock(userId);
        if (!string.IsNullOrEmpty(unreadNotifications))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = unreadNotifications,
                IsMeta = true
            });
        }
        var weeklyGuidance = BuildWeeklyGuidanceBlock(userMessage);
        if (!string.IsNullOrEmpty(weeklyGuidance))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = weeklyGuidance,
                IsMeta = true
            });
        }
        var taskPlanningGuidance = BuildTaskPlanningGuidanceBlock(userMessage);
        if (!string.IsNullOrEmpty(taskPlanningGuidance))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = taskPlanningGuidance,
                IsMeta = true
            });
        }
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        if (!string.IsNullOrWhiteSpace(excelMarkdown))
        {
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = ExcelSystemReminder,
                IsMeta = true
            });
            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "user",
                Content = excelMarkdown,
                IsMeta = true
            });
        }

        conversation.State = AgentState.Thinking;
        conversation.LastActivity = _timeProvider.GetUtcNow().UtcDateTime;
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
            conversation.LastActivity = _timeProvider.GetUtcNow().UtcDateTime;

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
            conversation.LastActivity = _timeProvider.GetUtcNow().UtcDateTime;

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

    private Task ExecuteReActLoop(IServiceProvider sp, Guid conversationId)
    {
        return _conversations.TryGetValue(conversationId, out var conversation)
            ? _productionAgentExecutor.ExecuteReActLoop(
                sp,
                conversation,
                new AgentExecutionOptions())
            : Task.CompletedTask;
    }

    private Task ExecuteAdviceAndResume(IServiceProvider sp, Guid conversationId, Guid adviceId)
    {
        return _conversations.TryGetValue(conversationId, out var conversation)
            ? _productionAgentExecutor.ExecuteAdviceAndResume(sp, conversation, adviceId)
            : Task.CompletedTask;
    }

    /// <summary>
    /// injected into the system-reminder via {currentDateTime}.
    /// </summary>

    private string GetCurrentDateTimeBlock()
    {
        var chinaNow = _timeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(8);
        var daysSinceMonday = ((int)chinaNow.DayOfWeek + 6) % 7;
        var monday = chinaNow.Date.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(6);
        return $"Current time: {chinaNow:dddd, MMMM d, yyyy, h:mm tt} (UTC+8)\n" +
               $"This week: {monday:yyyy-MM-dd} (Monday) – {sunday:yyyy-MM-dd} (Sunday)";
    }

    /// <summary>
    /// Builds a standalone &lt;system-reminder&gt; block that suggests calling the
    /// TaskPlanning subagent tool. Injected only when the user message exceeds
    /// 3 lines or 100 characters, indicating a potentially complex request that
    /// benefits from explicit planning before execution.
    /// Returns an empty string when the message is short and simple.
    /// </summary>
    private static string BuildTaskPlanningGuidanceBlock(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return string.Empty;

        var lineCount = userMessage.Split('\n').Length;
        var charCount = userMessage.Length;

        if (lineCount <= 3 && charCount <= 100)
            return string.Empty;

        return "<system-reminder>\n" +
               "The user's message is long or complex. Before executing any actions:\n" +
               "- Call the TaskPlanning tool first to break this into concrete, ordered steps.\n" +
               "- Pass the user's full request to TaskPlanning so it can search for relevant cards and build an accurate plan.\n" +
               "- After receiving the plan, execute each step in order using the available tools.\n" +
               "- Do NOT skip planning for multi-step requests — planning prevents mistakes.\n" +
               "│  IMPORTANT: this context may or may not be relevant    │\n" +
               "</system-reminder>";
    }

    /// <summary>
    /// Builds a standalone &lt;system-reminder&gt; block with weekly-summary
    /// guidance. Injected only when the user message contains "周" or "week"
    /// keywords, saving tokens on unrelated conversations.
    /// Returns an empty string when no weekly keywords are detected.
    /// </summary>
    private static string BuildWeeklyGuidanceBlock(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return string.Empty;

        var lower = userMessage.ToLowerInvariant();
        if (!lower.Contains("周") && !lower.Contains("week"))
            return string.Empty;

        return "<system-reminder>\n" +
               "The user is asking about a weekly summary or time period. To answer accurately:\n" +
               "- Use GetCardsByDateRange with dateType=\"completed\" and the Monday–Sunday range shown in the first system-reminder above.\n" +
               "- If user intend to summarize weekly report or this week's work, use GetCardsByDateRange tool instead of going through all the boards and cards.\n" +
               "- Do NOT guess dates — always use the exact week boundary from the system-reminder.\n" +
               "- If the user asks about a different week (e.g. \"last week\"), adjust the date range accordingly.\n" +
               "│  IMPORTANT: this context may or may not be relevant    │\n" +
               "</system-reminder>";
    }

    /// <summary>
    /// Builds a system-reminder block with current date/time and the user's most
    /// recently created cards (newest first). Card titles over 200 characters are
    /// truncated.
    /// </summary>
    private string BuildRecentCardsBlock(string userId, int count = 10)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var recentCards = db.KanbanCards
            .Include(c => c.Column)
            .ThenInclude(col => col.Board)
            .Where(c => c.Column.Board.UserId == userId || c.AssignedUserId == userId)
            .OrderByDescending(c => c.CreationTime)
            .Take(count)
            .Select(c => new
            {
                c.Title,
                ColumnName = c.Column.Name,
                BoardName = c.Column.Board.Name
            })
            .ToList();

        if (recentCards.Count == 0)
            return string.Empty;

        var dateTimeBlock = GetCurrentDateTimeBlock();
        var sb = new StringBuilder();
        sb.AppendLine("<system-reminder>");
        sb.AppendLine(dateTimeBlock);
        sb.AppendLine();
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
        using var scope = _scopeFactory.CreateScope();
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
    /// Builds a system-reminder block listing unread notification count and
    /// up to 10 most recent unread notification messages.
    /// Returns an empty string when there are no unread notifications.
    /// </summary>
    private string BuildUnreadNotificationsBlock(string userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var totalCount = db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .Count();

        if (totalCount == 0) return string.Empty;

        var notifications = db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .Include(n => n.Card!)
                .ThenInclude(c => c.Column)
                    .ThenInclude(col => col.Board)
            .Include(n => n.Board)
            .Include(n => n.ActorUser)
            .OrderByDescending(n => n.CreationTime)
            .Take(10)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<system-reminder>");
        if (totalCount == 1)
        {
            sb.AppendLine("You have 1 unread notification:");
        }
        else if (totalCount <= 10)
        {
            sb.AppendLine($"You have {totalCount} unread notifications:");
        }
        else
        {
            sb.AppendLine($"You have {totalCount} unread notifications. Showing the 10 most recent:");
        }

        foreach (var n in notifications)
        {
            var message = NotificationTemplateService.BuildMessage(n);
            var title = message.Length > 200
                ? message[..200] + "..."
                : message;
            var boardName = n.Board?.Name ?? n.Card?.Column.Board.Name ?? "(unknown)";
            var timeAgo = GetRelativeTimeBlock(n.CreationTime);
            sb.Append("- [").Append(n.Type).Append("] ");
            sb.Append('"').Append(title).Append('"');
            sb.Append(" (Board: \"").Append(boardName).Append("\", ");
            sb.Append(timeAgo).Append(')');
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Use GetUnreadNotifications tool if you need full details about any of these notifications.");
        sb.Append("|  IMPORTANT: this context may or may not be relevant    |\n</system-reminder>");

        return sb.ToString();
    }

    private string GetRelativeTimeBlock(DateTime utcTime)
    {
        var diff = _timeProvider.GetUtcNow().UtcDateTime - utcTime;
        return diff.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            < 43200 => $"{(int)diff.TotalDays}d ago",
            _ => utcTime.ToString("yyyy-MM-dd")
        };
    }

    /// <summary>
    /// Builds a context block injected into the system prompt via {userContext}.
    /// This information is NOT visible to the user in the chat UI — it lives
    /// in the system prompt so the LLM has the facts it needs without cluttering
    /// the conversation.
    /// </summary>
    private string BuildUserContextBlock(string userId, int boardId)
    {
        using var scope = _scopeFactory.CreateScope();
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
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var conversationCutoff = utcNow - ConversationTtl;
        var adviceCutoff = utcNow - AdviceTtl;

        foreach (var (id, conv) in _conversations)
        {
            if (conv.LastActivity < conversationCutoff && _conversations.TryRemove(id, out _))
                _adviceService.RemoveConversationAdvice(id);
        }

        // Also sweep orphaned advice (from conversations removed by CancelRun)
        _adviceService.RemoveExpiredAdvice(adviceCutoff);
    }

}
