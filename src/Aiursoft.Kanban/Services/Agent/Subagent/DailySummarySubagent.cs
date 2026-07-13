using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// A subagent that analyzes a user's daily Kanban activity and generates a summary essay.
/// It has read-only access to card and board tools to understand what happened today.
/// Not exposed as an MCP tool to the main agent — called directly by the background job.
/// </summary>
public class DailySummarySubagent : SubagentBase, ISingletonDependency
{
    public override string Name => "DailySummary";
    public override string Description =>
        "Generate a daily summary essay for a user based on their Kanban activity. " +
        "Analyze what was completed today, what remains, and produce a reflective summary.";

    protected override string SystemPrompt =>
        """
        You are a daily summary assistant for a Kanban board application.
        Your task is to analyze the user's activity today and produce a
        reflective daily summary essay.

        ## Process

        The user's assigned cards, completed cards for today, and board data
        are already provided in the <context> block above. You do NOT need to
        call GetMyTasks, GetUserBoards, or GetCardsByDateRange to discover
        basic information.

        Use tools ONLY when you need additional details:
        - GetCardById: to inspect a specific card's full details
        - GetCardsInColumn / GetColumns: to understand column structure
        - FilterCards: for custom queries beyond the provided context

        Analyze the provided data and produce a summary.

        ## Output Format

        Return a well-structured essay in the language specified in the user's request. Structure:

        **Greeting** (one line neutral greeting — avoid time-specific greetings since the report may be viewed at any time)

        **✅ Completed Today / 今日完成**
        List of cards that were completed today, citing board and column names.
        If nothing was completed, state that honestly and constructively.

        **📈 Progress Today / 今日进展**
        Cards that were started or progressed (moved to In Progress column).

        **🆕 New Tasks / 新任务**
        Cards created today that add to the workload.

        **📋 Remaining / 待处理**
        The top 3-5 remaining incomplete items with their priority and board.
        Highlight anything overdue or approaching its due date.

        **🧠 Reflection / 总结**
        1-2 sentences objectively assessing the day. What went well? What
        could have gone better? Be factual — if little was accomplished,
        acknowledge that without being negative. If a lot was done,
        celebrate the achievement.

        **🔭 Tomorrow's Outlook / 明日展望**
        1 sentence about what to tackle next based on remaining priorities.

        ## Guidelines

        - Keep the essay under 1200 characters.
        - Be factual — report what the data shows, not what you assume.
        - If the user has no activity today, suggest they review their boards.
        - All operations are performed as the current user.
        """;

    public override string[] ToolNames =>
    [
        "GetBoards",
        "GetMyTasks",
        "GetUserBoards",
        "GetBoardById",
        "GetCardsByDateRange",
        "FilterCards",
        "GetCardById",
        "GetCardsInColumn",
        "GetColumns",
        "GetCardsByPriority"
    ];

    protected override int MaxIterations => 8;

    protected override SemaphoreSlim ConcurrencyLimit => DailySubagentSemaphore;

    public DailySummarySubagent(
        ToolRegistry toolRegistry,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILoggerFactory loggerFactory)
        : base(toolRegistry, claudeClient, rootServices, loggerFactory)
    {
    }
}
