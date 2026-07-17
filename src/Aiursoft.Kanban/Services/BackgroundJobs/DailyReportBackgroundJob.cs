using System.Text;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

/// <summary>
/// Background job that scans users every 30 minutes and generates daily plans
/// (before 4pm UTC+8) or daily summaries (after 4pm UTC+8) via LLM subagents.
///
/// Change detection: if any card in the user's boards was created after the
/// last report's GeneratedAt timestamp, the report is regenerated.
/// </summary>
public class DailyReportBackgroundJob : IBackgroundJob
{
    private readonly TemplateDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly DailyPlanningSubagent _planningSubagent;
    private readonly DailySummarySubagent _summarySubagent;
    private readonly ILogger<DailyReportBackgroundJob> _logger;

    private const int MaxUsersPerRun = 20;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public string Name => "Daily Report Generator";

    public string Description =>
        "Every 30 minutes, scans users who need a daily plan (morning, before 4pm UTC+8) " +
        "or daily summary (afternoon, after 4pm UTC+8). Generates content via LLM subagents " +
        "with read-only Kanban tools and stores the reports. Regenerates when board activity " +
        "is detected after the last report.";

    public DailyReportBackgroundJob(
        TemplateDbContext db,
        UserManager<User> userManager,
        DailyPlanningSubagent planningSubagent,
        DailySummarySubagent summarySubagent,
        ILogger<DailyReportBackgroundJob> logger)
    {
        _db = db;
        _userManager = userManager;
        _planningSubagent = planningSubagent;
        _summarySubagent = summarySubagent;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var chinaNow = nowUtc + ChinaOffset;
        var todayChina = chinaNow.Date;

        _logger.LogInformation(
            "DailyReportBackgroundJob starting: UTC={UtcNow}, China={ChinaNow}, ChinaDate={ChinaDate}",
            nowUtc.ToString("O"), chinaNow.ToString("O"), todayChina.ToString("yyyy-MM-dd"));

        // Planning window: before 4pm UTC+8
        if (chinaNow.Hour < 16)
        {
            await ProcessGeneration(DailyReportType.Plan, todayChina);
        }

        // Summary window: after 4pm UTC+8
        if (chinaNow.Hour >= 16)
        {
            await ProcessGeneration(DailyReportType.Summary, todayChina);
        }

        _logger.LogInformation("DailyReportBackgroundJob completed.");
    }

    private async Task ProcessGeneration(DailyReportType reportType, DateTime todayChina)
    {
        var usersNeedingGeneration = await GetUsersNeedingGeneration(reportType, todayChina);

        _logger.LogInformation(
            "Found {Count} users needing {ReportType} generation for {Date}",
            usersNeedingGeneration.Count, reportType, todayChina.ToString("yyyy-MM-dd"));

        var processed = 0;
        foreach (var userId in usersNeedingGeneration)
        {
            if (processed >= MaxUsersPerRun) break;

            try
            {
                await GenerateAndSaveReport(userId, reportType, todayChina);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate {ReportType} for user {UserId}", reportType, userId);
            }
        }

        _logger.LogInformation(
            "Processed {Count} {ReportType} generation(s)", processed, reportType);
    }

    /// <summary>
    /// Finds users who need a report generated for a given type and date.
    /// A user needs generation if:
    /// 1. No report exists for that type + date, OR
    /// 2. Cards were created after the report's GeneratedAt timestamp.
    /// </summary>
    private async Task<List<string>> GetUsersNeedingGeneration(
        DailyReportType reportType, DateTime todayChina)
    {
        // Find all users who own boards (the primary audience for daily reports)
        var activeUserIds = await _db.KanbanBoards
            .Where(b => b.UserId != null)
            .Select(b => b.UserId!)
            .Distinct()
            .ToListAsync();

        var needingGeneration = new List<string>();

        foreach (var userId in activeUserIds)
        {
            var existingReport = await _db.DailyReports
                .Where(r => r.UserId == userId
                         && r.Date == todayChina
                         && r.ReportType == reportType)
                .FirstOrDefaultAsync();

            if (existingReport == null)
            {
                // No report for today yet — always generate
                needingGeneration.Add(userId);
                continue;
            }

            // Check if any cards were created after the report was generated
            var userBoardIds = await _db.KanbanBoards
                .Where(b => b.UserId == userId)
                .Select(b => b.Id)
                .ToListAsync();

            if (userBoardIds.Count == 0) continue;

            var columnIds = await _db.KanbanColumns
                .Where(c => userBoardIds.Contains(c.BoardId))
                .Select(c => c.Id)
                .ToListAsync();

            var hasNewerCards = await _db.KanbanCards
                .AnyAsync(c => columnIds.Contains(c.ColumnId)
                            && c.CreationTime > existingReport.GeneratedAt);

            if (hasNewerCards)
            {
                needingGeneration.Add(userId);
            }
        }

        return needingGeneration;
    }

    /// <summary>
    /// Builds a context block with all cards assigned to the user, sorted by priority.
    /// Injected into the subagent prompt so it doesn't need to spend iterations on
    /// tool calls to discover basic card data.
    /// </summary>
    internal static async Task<string> BuildCardContextAsync(
        TemplateDbContext db, UserManager<User> userManager, string userId, DailyReportType reportType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<context>");
        sb.AppendLine("The following is the current state of the user's Kanban boards and assigned cards.");
        sb.AppendLine("Use this data directly — you do NOT need to call tools (GetMyTasks, GetUserBoards,");
        sb.AppendLine("GetOverdueCards, GetCardsByPriority, etc.) to discover this basic information.");
        sb.AppendLine("Skip directly to analyzing and producing the report.");
        sb.AppendLine();

        // ── User's boards (owned + shared) with ≥1 card assigned to this user ──
        var user = await userManager.FindByIdAsync(userId);
        var userRoleIds = user != null
            ? (await userManager.GetRolesAsync(user))
                .Select(rn => db.Roles.FirstOrDefault(r => r.Name == rn)?.Id)
                .Where(id => id != null)
                .Select(id => id!)
                .ToList()
            : [];

        var ownedBoardIds = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Select(b => b.Id)
            .ToListAsync();

        var sharedBoardIds = await db.BoardShares
            .Where(s => s.SharedWithUserId == userId ||
                        (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .Select(s => s.BoardId)
            .Distinct()
            .ToListAsync();

        var allAccessibleBoardIds = ownedBoardIds
            .Concat(sharedBoardIds)
            .Distinct()
            .ToList();

        // Only include boards that have at least 1 incomplete card assigned to this user
        var relevantBoards = await db.KanbanBoards
            .Where(b => allAccessibleBoardIds.Contains(b.Id)
                && db.KanbanCards.Any(c => c.Column.BoardId == b.Id
                    && c.AssignedUserId == userId
                    && c.Column.ColumnStatus != ColumnStatus.Completed))
            .Select(b => new { b.Id, b.Name })
            .ToListAsync();

        if (relevantBoards.Count > 0)
        {
            sb.AppendLine("## Your Boards (with cards assigned to you)");
            foreach (var board in relevantBoards)
            {
                var assignedToYou = await db.KanbanCards
                    .CountAsync(c => c.Column.BoardId == board.Id
                        && c.AssignedUserId == userId
                        && c.Column.ColumnStatus != ColumnStatus.Completed);
                sb.AppendLine($"- **{board.Name}** (ID: {board.Id}): {assignedToYou} card(s) assigned to you");
            }
            sb.AppendLine();
        }

        // ── Incomplete cards assigned to user, sorted by priority then due date ──
        var now = DateTime.UtcNow;
        var incompleteCards = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Where(c => c.AssignedUserId == userId
                && c.Column.ColumnStatus != ColumnStatus.Completed)
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.DueDate == null ? 1 : 0)
            .ThenBy(c => c.DueDate)
            .ThenBy(c => c.Title)
            .ToListAsync();

        if (incompleteCards.Count > 0)
        {
            sb.AppendLine("## Incomplete Cards Assigned to You (sorted by priority)");
            foreach (var card in incompleteCards)
            {
                var dueStr = card.DueDate.HasValue
                    ? $" (Due: {card.DueDate:yyyy-MM-dd})"
                    : "";
                var overdue = card.DueDate.HasValue && card.DueDate.Value < now.Date
                    ? " ⚠️ OVERDUE"
                    : "";
                sb.AppendLine($"- [P{card.Priority}] \"{card.Title}\" in **{card.Column.Name}** on board **{card.Column.Board.Name}**{dueStr}{overdue}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Incomplete Cards Assigned to You");
            sb.AppendLine("(none)");
            sb.AppendLine();
        }

        // ── Overdue highlights ──
        var overdueCards = incompleteCards
            .Where(c => c.DueDate.HasValue && c.DueDate.Value < now.Date)
            .ToList();

        if (overdueCards.Count > 0)
        {
            sb.AppendLine("## ⚠️ Overdue Cards (past due date, needs immediate attention)");
            foreach (var card in overdueCards)
            {
                var daysOverdue = (now.Date - card.DueDate!.Value.Date).Days;
                sb.AppendLine($"- [P{card.Priority}] \"{card.Title}\" on board **{card.Column.Board.Name}** — Due {card.DueDate:yyyy-MM-dd} ({daysOverdue} day(s) overdue!)");
            }
            sb.AppendLine();
        }

        // ── Summary: recently completed cards ──
        if (reportType == DailyReportType.Summary)
        {
            var todayStart = now.Date;
            var completedToday = await db.KanbanCards
                .Include(c => c.Column).ThenInclude(col => col.Board)
                .Where(c => c.AssignedUserId == userId
                    && c.Column.ColumnStatus == ColumnStatus.Completed
                    && c.ActualEndTime >= todayStart)
                .OrderByDescending(c => c.ActualEndTime)
                .ToListAsync();

            if (completedToday.Count > 0)
            {
                sb.AppendLine("## Completed Today");
                foreach (var card in completedToday)
                {
                    sb.AppendLine($"- [P{card.Priority}] \"{card.Title}\" in **{card.Column.Name}** on board **{card.Column.Board.Name}**");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## Completed Today");
                sb.AppendLine("(none)");
                sb.AppendLine();
            }
        }

        sb.AppendLine("</context>");
        return sb.ToString();
    }

    private async Task GenerateAndSaveReport(
        string userId, DailyReportType reportType, DateTime todayChina)
    {
        var subagent = reportType == DailyReportType.Plan
            ? (ISubagent)_planningSubagent
            : _summarySubagent;

        var user = await _userManager.FindByIdAsync(userId);
        var language = GetLanguageName(user?.DailyReportLanguage ?? "en");

        var chinaNow = DateTime.UtcNow + TimeSpan.FromHours(8);
        var cardContext = await BuildCardContextAsync(_db, _userManager, userId, reportType);
        var prompt = reportType == DailyReportType.Plan
            ? cardContext +
              $"\nGenerate a daily plan for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              $"Analyze the user's boards and tasks above, then produce a structured plan essay in {language}."
            : cardContext +
              $"\nGenerate a daily summary for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              $"Review the data above to understand what the user completed and what remains, then produce a structured summary essay in {language}.";

        _logger.LogInformation(
            "Calling subagent {SubagentName} for user {UserId}",
            subagent.Name, userId);

        string content;
        try
        {
            content = await subagent.ExecuteAsync(userId, prompt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Subagent {SubagentName} failed for user {UserId}",
                subagent.Name, userId);
            content = reportType == DailyReportType.Plan
                ? "无法生成今日计划。请稍后再试。\n\n(Plan generation failed. Please try again later.)"
                : "无法生成今日总结。请稍后再试。\n\n(Summary generation failed. Please try again later.)";
        }

        // Upsert: remove existing report for this user + date + type, then insert new one.
        var existing = await _db.DailyReports
            .Where(r => r.UserId == userId
                     && r.Date == todayChina
                     && r.ReportType == reportType)
            .ToListAsync();

        if (existing.Count > 0)
        {
            _db.DailyReports.RemoveRange(existing);
        }

        var report = new DailyReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ReportType = reportType,
            Content = content,
            Date = todayChina,
            GeneratedAt = DateTime.UtcNow
        };

        _db.DailyReports.Add(report);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Saved {ReportType} for user {UserId}, content length={Length} chars",
            reportType, userId, content.Length);
    }

    /// <summary>
    /// Maps a DailyReportLanguage code to a human-readable language name for LLM prompts.
    /// </summary>
    internal static string GetLanguageName(string code) => code switch
    {
        "zh" => "Chinese (中文)",
        "ja" => "Japanese (日本語)",
        "ko" => "Korean (한국어)",
        _ => "English"
    };
}
