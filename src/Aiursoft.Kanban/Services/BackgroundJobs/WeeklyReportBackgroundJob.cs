using System.Text;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

/// <summary>
/// Background job that runs every hour. On Friday afternoon (UTC ≥ 6:00, i.e. UTC+8 ≥ 2pm),
/// it generates a weekly report for each eligible user who has completed cards this week
/// and does not already have a report for the current week.
///
/// Users can discard their report, which causes regeneration on the next hourly run.
/// </summary>
public class WeeklyReportBackgroundJob : IBackgroundJob
{
    private readonly TemplateDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly WeeklyReportSubagent _weeklySubagent;
    private readonly ILogger<WeeklyReportBackgroundJob> _logger;

    private const int MaxUsersPerRun = 20;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public string Name => "Weekly Report Generator";

    public string Description =>
        "Every hour, checks if it's Friday afternoon (UTC+8 ≥ 2pm). " +
        "If so, generates a weekly report for each eligible user who has completed cards " +
        "this week and does not already have a report. " +
        "Discarded reports are regenerated on the next run.";

    public WeeklyReportBackgroundJob(
        TemplateDbContext db,
        UserManager<User> userManager,
        WeeklyReportSubagent weeklySubagent,
        ILogger<WeeklyReportBackgroundJob> logger)
    {
        _db = db;
        _userManager = userManager;
        _weeklySubagent = weeklySubagent;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var chinaNow = nowUtc + ChinaOffset;

        _logger.LogInformation(
            "WeeklyReportBackgroundJob starting: UTC={UtcNow}, China={ChinaNow}",
            nowUtc.ToString("O"), chinaNow.ToString("O"));

        // Only run on Friday afternoon (UTC ≥ 6:00, i.e. UTC+8 ≥ 14:00)
        if (nowUtc.DayOfWeek != DayOfWeek.Friday || nowUtc.Hour < 6)
        {
            _logger.LogInformation(
                "WeeklyReportBackgroundJob: Not Friday afternoon (UTC={Utc}), skipping.",
                nowUtc.ToString("O"));
            return;
        }

        var weekStart = GetCurrentWeekStart(chinaNow);

        _logger.LogInformation(
            "WeeklyReportBackgroundJob: Friday afternoon detected. Generating reports for week starting {WeekStart}.",
            weekStart.ToString("yyyy-MM-dd"));

        await ProcessGeneration(weekStart);
        _logger.LogInformation("WeeklyReportBackgroundJob completed.");
    }

    private async Task ProcessGeneration(DateTime weekStart)
    {
        var userIds = await GetEligibleUsers(weekStart);

        _logger.LogInformation(
            "Found {Count} users needing weekly report for week {WeekStart}",
            userIds.Count, weekStart.ToString("yyyy-MM-dd"));

        var processed = 0;
        foreach (var userId in userIds)
        {
            if (processed >= MaxUsersPerRun) break;

            try
            {
                await GenerateAndSaveReport(userId, weekStart);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to generate weekly report for user {UserId}", userId);
            }
        }

        _logger.LogInformation(
            "Processed {Count} weekly report(s)", processed);
    }

    /// <summary>
    /// Gets the Monday (midnight UTC) of the current UTC+8 week.
    /// </summary>
    public static DateTime GetCurrentWeekStart(DateTime chinaNow)
    {
        var daysSinceMonday = ((int)chinaNow.DayOfWeek + 6) % 7;
        var monday = chinaNow.Date.AddDays(-daysSinceMonday);
        // Convert back to UTC midnight (the date is already a UTC+8 date, so subtract the offset)
        return monday;
    }

    /// <summary>
    /// Finds users who need a weekly report: they have completed cards this week
    /// and don't already have a report for this week.
    /// </summary>
    private async Task<List<string>> GetEligibleUsers(DateTime weekStart)
    {
        var userIds = new HashSet<string>();

        // Collect all users with activity in the system
        // 1. Board owners
        var ownerIds = await _db.KanbanBoards
            .Where(b => b.UserId != null)
            .Select(b => b.UserId!)
            .ToListAsync();
        foreach (var id in ownerIds) userIds.Add(id);

        // 2. Card assignees
        var assigneeIds = await _db.KanbanCards
            .Where(c => c.AssignedUserId != null)
            .Select(c => c.AssignedUserId!)
            .Distinct()
            .ToListAsync();
        foreach (var id in assigneeIds) userIds.Add(id);

        // 3. Direct share recipients
        var sharedUserIds = await _db.BoardShares
            .Where(s => s.SharedWithUserId != null)
            .Select(s => s.SharedWithUserId!)
            .ToListAsync();
        foreach (var id in sharedUserIds) userIds.Add(id);

        // 4. Role-based share recipients
        var sharedRoleIds = await _db.BoardShares
            .Where(s => s.SharedWithRoleId != null)
            .Select(s => s.SharedWithRoleId!)
            .Distinct()
            .ToListAsync();

        if (sharedRoleIds.Count > 0)
        {
            var roleUserIds = await _db.UserRoles
                .Where(ur => sharedRoleIds.Contains(ur.RoleId))
                .Select(ur => ur.UserId)
                .Distinct()
                .ToListAsync();
            foreach (var id in roleUserIds) userIds.Add(id);
        }

        // Filter: only users without existing report AND with completed cards this week

        // Pre-fetch users who opted in to weekly reports (default true, users may opt out)
        var optedInUserIds = await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.EnableWeeklyReport)
            .Select(u => u.Id)
            .ToListAsync();
        var optedInSet = new HashSet<string>(optedInUserIds);

        var eligibleUsers = new List<string>();
        var weekEnd = weekStart.AddDays(7); // Monday of next week

        foreach (var userId in userIds)
        {
            // Skip if user opted out of weekly reports
            if (!optedInSet.Contains(userId)) continue;

            // Skip if already has a report for this week
            var hasReport = await _db.WeeklyReports
                .AnyAsync(r => r.UserId == userId && r.WeekStart == weekStart);
            if (hasReport) continue;

            // Check if user has completed cards this week
            var hasCompletedCards = await _db.KanbanCards
                .AnyAsync(c => c.AssignedUserId == userId
                            && c.Column.ColumnStatus == ColumnStatus.Completed
                            && c.ActualEndTime >= weekStart
                            && c.ActualEndTime < weekEnd);

            if (hasCompletedCards)
            {
                eligibleUsers.Add(userId);
            }
        }

        return eligibleUsers;
    }

    /// <summary>
    /// Builds a context block with all cards completed by the user this week.
    /// </summary>
    internal static async Task<string> BuildWeeklyCardContextAsync(
        TemplateDbContext db, string userId, DateTime weekStart)
    {
        var weekEnd = weekStart.AddDays(7);
        var sb = new StringBuilder();
        sb.AppendLine("<context>");
        sb.AppendLine("The following cards were completed by the user during the current week");
        sb.AppendLine($"({weekStart:yyyy-MM-dd} Monday – {weekStart.AddDays(6):yyyy-MM-dd} Sunday, UTC+8).");
        sb.AppendLine("Use this data directly to produce the weekly report — you do NOT need to call");
        sb.AppendLine("GetMyTasks, GetUserBoards, or GetCardsByDateRange.");
        sb.AppendLine();

        var completedCards = await db.KanbanCards
            .Include(c => c.Column).ThenInclude(col => col.Board)
            .Where(c => c.AssignedUserId == userId
                && c.Column.ColumnStatus == ColumnStatus.Completed
                && c.ActualEndTime >= weekStart
                && c.ActualEndTime < weekEnd)
            .OrderByDescending(c => c.ActualEndTime)
            .ToListAsync();

        if (completedCards.Count > 0)
        {
            sb.AppendLine("## Completed Cards This Week");
            foreach (var card in completedCards)
            {
                var description = !string.IsNullOrWhiteSpace(card.Description)
                    ? $" — {TruncateForContext(card.Description, 200)}"
                    : "";
                sb.AppendLine($"- \"{card.Title}\" on board **{card.Column.Board.Name}** (completed {card.ActualEndTime:yyyy-MM-dd}){description}");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Completed Cards This Week");
            sb.AppendLine("(none)");
            sb.AppendLine();
        }

        sb.AppendLine("</context>");
        return sb.ToString();
    }

    private static string TruncateForContext(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLen ? value : value[..maxLen] + "…";
    }

    private async Task GenerateAndSaveReport(string userId, DateTime weekStart)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var language = DailyReportBackgroundJob.GetLanguageName(user?.DailyReportLanguage ?? "en");

        var chinaNow = DateTime.UtcNow + ChinaOffset;
        var cardContext = await BuildWeeklyCardContextAsync(_db, userId, weekStart);
        var prompt = cardContext +
                     $"\nGenerate a weekly report for the week of {weekStart:yyyy-MM-dd} (Monday–Sunday, UTC+8). " +
                     $"Current time: {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
                     $"Review the completed cards above and produce a concise bullet-point summary in {language}.";

        _logger.LogInformation(
            "Calling subagent WeeklyReport for user {UserId}", userId);

        string content;
        try
        {
            content = await _weeklySubagent.ExecuteAsync(userId, prompt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Subagent WeeklyReport failed for user {UserId}", userId);
            content = "无法生成本周周报。请稍后再试。\n\n(Weekly report generation failed. Please try again later.)";
        }

        var report = new WeeklyReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            WeekStart = weekStart,
            GeneratedAt = DateTime.UtcNow
        };

        _db.WeeklyReports.Add(report);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Saved weekly report for user {UserId}, week {WeekStart}, content length={Length} chars",
            userId, weekStart.ToString("yyyy-MM-dd"), content.Length);
    }
}
