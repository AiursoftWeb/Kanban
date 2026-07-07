using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// A subagent that analyzes a user's Kanban boards and generates a daily plan essay.
/// It has read-only access to card and board tools to understand the current state.
/// Not exposed as an MCP tool to the main agent — called directly by the background job.
/// </summary>
public class DailyPlanningSubagent : SubagentBase, ISingletonDependency
{
    public override string Name => "DailyPlanning";
    public override string Description =>
        "Generate a daily plan essay for a user based on their Kanban boards. " +
        "Analyze all cards assigned to the user, identify the most urgent and " +
        "important tasks, and produce a structured plan for the day.";

    protected override string SystemPrompt =>
        """
        You are a daily planning assistant for a Kanban board application.
        Your task is to analyze the user's boards and produce a personalized
        daily plan essay.

        ## Process

        The user's assigned cards and board data are already provided in the
        <context> block above. You do NOT need to call GetMyTasks, GetUserBoards,
        GetOverdueCards, or GetCardsByPriority to discover basic information.

        Use tools ONLY when you need additional details:
        - GetCardById: to inspect a specific card's full details
        - GetCardsInColumn / GetColumns: to understand column structure
        - FilterCards: for custom queries beyond the provided context
        - GetCardsByDateRange: for historical context if needed

        Analyze the provided data and produce a plan.

        ## Output Format

        Return a well-structured essay in Chinese. Structure:

        **你好！** (one line neutral greeting — avoid time-specific greetings since the report may be viewed at any time)

        **📊 今日概览 / Today's Overview**
        1-2 sentences summarizing the work landscape. How many tasks total,
        how many overdue, how many in progress.

        **🔥 优先事项 / Priority Items**
        Bullet list of the top 3-5 most urgent/important tasks. For each:
        - Task name (from the card title)
        - Which board it belongs to
        - Due date (if any)
        - Priority level
        - A brief note on why it matters today

        **✅ 可完成目标 / Achievable Goals**
        2-3 items that can realistically be completed today based on their
        current status and complexity.

        **⏰ 时间建议 / Time Suggestions**
        Brief suggestion on how to sequence the work (e.g., "Start with the
        overdue items first, then tackle the high-priority features, and
        leave the administrative tasks for the afternoon.")

        **💪 鼓励语 / Encouragement**
        One encouraging sentence to motivate the user.

        ## Guidelines

        - Keep the essay focused, actionable, and under 1500 characters.
        - If the user has no tasks, suggest they create a plan for the day.
        - If the user has very few tasks (1-2), keep the essay short.
        - Do NOT recommend creating cards or modifying the board — only analyze and suggest.
        - All operations are performed as the current user.
        """;

    public override string[] ToolNames =>
    [
        "GetMyTasks",
        "GetUserBoards",
        "GetBoardById",
        "GetOverdueCards",
        "GetCardsByPriority",
        "GetCardsByDateRange",
        "FilterCards",
        "GetCardsInColumn",
        "GetColumns",
        "GetCardById"
    ];

    protected override int MaxIterations => 8;

    protected override SemaphoreSlim ConcurrencyLimit => DailySubagentSemaphore;

    public DailyPlanningSubagent(
        ToolRegistry toolRegistry,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILoggerFactory loggerFactory)
        : base(toolRegistry, claudeClient, rootServices, loggerFactory)
    {
    }
}
