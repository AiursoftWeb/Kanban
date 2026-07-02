using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent.Subagent;
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
        DailyPlanningSubagent planningSubagent,
        DailySummarySubagent summarySubagent,
        ILogger<DailyReportBackgroundJob> logger)
    {
        _db = db;
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

    private async Task GenerateAndSaveReport(
        string userId, DailyReportType reportType, DateTime todayChina)
    {
        var subagent = reportType == DailyReportType.Plan
            ? (ISubagent)_planningSubagent
            : _summarySubagent;

        var prompt = reportType == DailyReportType.Plan
            ? $"Generate a daily plan for {todayChina:yyyy-MM-dd} (China timezone, UTC+8). " +
              "Analyze the user's boards and tasks, then produce a structured morning plan essay in Chinese."
            : $"Generate a daily summary for {todayChina:yyyy-MM-dd} (China timezone, UTC+8). " +
              "Review what the user completed and what remains, then produce a structured afternoon summary essay in Chinese.";

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
}
