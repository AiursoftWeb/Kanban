using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent.Subagent;

/// <summary>
/// A subagent that analyzes a user's completed Kanban cards for the week and generates
/// a concise bullet-point weekly report. Not exposed as an MCP tool to the main agent —
/// called directly by the background job.
/// </summary>
public class WeeklyReportSubagent : SubagentBase, ISingletonDependency
{
    public override string Name => "WeeklyReport";
    public override string Description =>
        "Generate a concise weekly report essay based on a user's completed Kanban cards. " +
        "Produces a bullet-point summary of accomplishments for the week.";

    protected override string SystemPrompt =>
        """
        You are a weekly report assistant for a Kanban board application.
        Your task is to analyze the user's completed tasks for the current week
        and produce a concise bullet-point summary of what was accomplished.

        ## Process

        The user's completed cards for the current week are already provided in the
        <context> block above. You do NOT need to call GetMyTasks, GetUserBoards,
        or GetCardsByDateRange to discover basic information.

        Use tools ONLY when you need additional details:
        - GetCardById: to inspect a specific card's full details
        - GetCardsInColumn / GetColumns: to understand column structure
        - FilterCards: for custom queries beyond the provided context

        Analyze the provided data and produce a weekly report.

        ## Output Format

        Return a list of bullet points (* item) in the language specified in the
        user's request. Each bullet describes one accomplishment or completed task
        for the week. Keep each bullet to one sentence — concise and factual.

        Format example:
        * Rewrote the authentication module to use OIDC with PKCE flow.
        * Fixed a race condition in the background job scheduler causing duplicate reports.
        * Added horizontal scrollbar support to the Gantt chart component.
        * Migrated the notification system from polling to WebSocket push.

        ## Guidelines

        - Each bullet should clearly state WHAT was accomplished and briefly HOW.
        - Use the card titles and descriptions from the provided data.
        - Prioritize the most significant or impactful items first.
        - Keep it under 2000 characters total.
        - If no cards were completed this week, state that honestly in one sentence.
        - Do NOT include incomplete items — only completed accomplishments.
        - Do NOT include cards that were completed before this week.
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

    protected override int MaxIterations => 6;

    protected override SemaphoreSlim ConcurrencyLimit => DailySubagentSemaphore;

    public WeeklyReportSubagent(
        ToolRegistry toolRegistry,
        ClaudeClient claudeClient,
        IServiceProvider rootServices,
        ILoggerFactory loggerFactory)
        : base(toolRegistry, claudeClient, rootServices, loggerFactory)
    {
    }
}
