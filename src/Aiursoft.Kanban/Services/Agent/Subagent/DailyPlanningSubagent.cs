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

        1. Use GetMyTasks to see the user's pending tasks (incomplete).
        2. Use GetUserBoards to list all boards the user owns.
        3. For each board with tasks, use GetCardsInColumn to browse columns
           and identify tasks in progress or not yet started.
        4. Use GetOverdueCards to find overdue items on each board.
        5. Use GetCardsByPriority to identify urgent items (priority 0-1).
        6. Analyze the data and produce a plan.

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
