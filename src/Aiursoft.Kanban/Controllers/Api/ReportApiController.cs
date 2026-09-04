using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/reports")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class ReportApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    DailyPlanningSubagent planningSubagent,
    DailySummarySubagent summarySubagent,
    WeeklyReportSubagent weeklySubagent) : ControllerBase
{
    private const int PageSize = 10;
    private const int MaxDisplayItems = 100;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    [HttpGet("daily")]
    public async Task<IActionResult> DailyReports([FromQuery] int page = 1)
    {
        var userId = CurrentUserId();
        var query = db.DailyReports
            .Where(report => report.UserId == userId)
            .OrderByDescending(report => report.Date)
            .ThenBy(report => report.ReportType);
        var (currentPage, totalPages, totalCount) = await PageInfoAsync(query, page);
        var reports = await query
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(report => new DailyReportDto
            {
                Id = report.Id,
                ReportType = report.ReportType.ToString(),
                Content = report.Content,
                Date = report.Date,
                GeneratedAt = report.GeneratedAt
            })
            .ToListAsync();
        var todayChina = (DateTime.UtcNow + ChinaOffset).Date;
        var todayReports = await db.DailyReports
            .Where(report => report.UserId == userId && report.Date == todayChina)
            .Select(report => new DailyReportDto
            {
                Id = report.Id,
                ReportType = report.ReportType.ToString(),
                Content = report.Content,
                Date = report.Date,
                GeneratedAt = report.GeneratedAt
            })
            .ToListAsync();
        var hasAccessibleBoards = await HasAccessibleBoardsAsync(userId);
        var chinaNow = DateTime.UtcNow + ChinaOffset;

        return this.Protocol(new DailyReportListResponse
        {
            Code = Code.ResultShown,
            Message = "Daily reports.",
            Reports = reports,
            TodayPlan = todayReports.FirstOrDefault(report => report.ReportType == nameof(DailyReportType.Plan)),
            TodaySummary = todayReports.FirstOrDefault(report => report.ReportType == nameof(DailyReportType.Summary)),
            CanGeneratePlan = chinaNow.Hour < 16 && hasAccessibleBoards,
            CanGenerateSummary = chinaNow.Hour >= 16 && hasAccessibleBoards,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    [HttpGet("daily/{reportId:guid}")]
    public async Task<IActionResult> DailyReport(Guid reportId)
    {
        var userId = CurrentUserId();
        var report = await db.DailyReports
            .Where(item => item.Id == reportId && item.UserId == userId)
            .Select(item => new DailyReportDto
            {
                Id = item.Id,
                ReportType = item.ReportType.ToString(),
                Content = item.Content,
                Date = item.Date,
                GeneratedAt = item.GeneratedAt
            })
            .SingleOrDefaultAsync();
        if (report == null)
        {
            return this.Protocol(Code.NotFound, "Daily report not found.");
        }

        return this.Protocol(new DailyReportResponse
        {
            Code = Code.ResultShown,
            Message = "Daily report loaded.",
            Report = report
        });
    }

    [HttpGet("weekly")]
    public async Task<IActionResult> WeeklyReports([FromQuery] int page = 1)
    {
        var userId = CurrentUserId();
        var query = db.WeeklyReports
            .Where(report => report.UserId == userId)
            .OrderByDescending(report => report.WeekStart)
            .ThenByDescending(report => report.GeneratedAt);
        var (currentPage, totalPages, totalCount) = await PageInfoAsync(query, page);
        var reports = await query
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(report => new WeeklyReportDto
            {
                Id = report.Id,
                Content = report.Content,
                WeekStart = report.WeekStart,
                GeneratedAt = report.GeneratedAt
            })
            .ToListAsync();
        var chinaNow = DateTime.UtcNow + ChinaOffset;
        var currentWeekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(chinaNow);
        var currentWeekReport = await db.WeeklyReports
            .Where(report => report.UserId == userId && report.WeekStart == currentWeekStart)
            .Select(report => new WeeklyReportDto
            {
                Id = report.Id,
                Content = report.Content,
                WeekStart = report.WeekStart,
                GeneratedAt = report.GeneratedAt
            })
            .SingleOrDefaultAsync();
        var nowUtc = DateTime.UtcNow;

        return this.Protocol(new WeeklyReportListResponse
        {
            Code = Code.ResultShown,
            Message = "Weekly reports.",
            Reports = reports,
            CurrentWeekReport = currentWeekReport,
            CurrentWeekStart = currentWeekStart,
            CanGenerate = nowUtc.DayOfWeek == DayOfWeek.Friday &&
                nowUtc.Hour >= 6 &&
                currentWeekReport == null &&
                await HasAccessibleBoardsAsync(userId),
            CurrentPage = currentPage,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    [HttpGet("weekly/{reportId:guid}")]
    public async Task<IActionResult> WeeklyReport(Guid reportId)
    {
        var userId = CurrentUserId();
        var report = await db.WeeklyReports
            .Where(item => item.Id == reportId && item.UserId == userId)
            .Select(item => new WeeklyReportDto
            {
                Id = item.Id,
                Content = item.Content,
                WeekStart = item.WeekStart,
                GeneratedAt = item.GeneratedAt
            })
            .SingleOrDefaultAsync();
        if (report == null)
        {
            return this.Protocol(Code.NotFound, "Weekly report not found.");
        }

        return this.Protocol(new WeeklyReportResponse
        {
            Code = Code.ResultShown,
            Message = "Weekly report loaded.",
            Report = report
        });
    }

    [HttpPost("daily/{type}/generate")]
    public async Task<IActionResult> GenerateDaily(string type)
    {
        var userId = CurrentUserId();
        var chinaNow = DateTime.UtcNow + ChinaOffset;
        var reportType = type.Trim().ToLowerInvariant() switch
        {
            "plan" when chinaNow.Hour < 16 => DailyReportType.Plan,
            "summary" when chinaNow.Hour >= 16 => DailyReportType.Summary,
            _ => (DailyReportType?)null
        };
        if (!reportType.HasValue)
        {
            return this.Protocol(Code.InvalidInput,
                "Daily plans are available before 16:00 and summaries after 16:00 (UTC+8).");
        }
        if (!await HasAccessibleBoardsAsync(userId))
        {
            return this.Protocol(Code.InvalidInput, "No accessible boards are available for this report.");
        }

        var todayChina = chinaNow.Date;
        var existing = await db.DailyReports
            .Where(report => report.UserId == userId &&
                report.Date == todayChina &&
                report.ReportType == reportType.Value)
            .ToListAsync();
        if (existing.Count > 0)
        {
            db.DailyReports.RemoveRange(existing);
        }

        ISubagent subagent = reportType.Value == DailyReportType.Plan
            ? planningSubagent
            : summarySubagent;
        var user = await userManager.FindByIdAsync(userId);
        var language = DailyReportBackgroundJob.GetLanguageName(user?.DailyReportLanguage ?? "en");
        var cardContext = await DailyReportBackgroundJob.BuildCardContextAsync(
            db,
            userManager,
            userId,
            reportType.Value);
        var prompt = reportType.Value == DailyReportType.Plan
            ? cardContext +
              $"\nGenerate a daily plan for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              $"Analyze the data above, then produce a structured plan essay in {language}."
            : cardContext +
              $"\nGenerate a daily summary for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              $"Review the data above, then produce a structured summary essay in {language}.";
        string content;
        try
        {
            content = await subagent.ExecuteAsync(userId, prompt, CancellationToken.None);
        }
        catch
        {
            content = reportType.Value == DailyReportType.Plan
                ? "无法生成今日计划。请稍后再试。\n\n(Plan generation failed. Please try again later.)"
                : "无法生成今日总结。请稍后再试。\n\n(Summary generation failed. Please try again later.)";
        }

        var report = new DailyReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ReportType = reportType.Value,
            Content = content,
            Date = todayChina,
            GeneratedAt = DateTime.UtcNow
        };
        db.DailyReports.Add(report);
        await db.SaveChangesAsync();
        return this.Protocol(new DailyReportResponse
        {
            Code = Code.JobDone,
            Message = existing.Count == 0 ? "Daily report generated." : "Daily report regenerated.",
            Report = ToDto(report)
        });
    }

    [HttpPost("weekly/generate")]
    public async Task<IActionResult> GenerateWeekly()
    {
        var userId = CurrentUserId();
        var nowUtc = DateTime.UtcNow;
        if (nowUtc.DayOfWeek != DayOfWeek.Friday || nowUtc.Hour < 6)
        {
            return this.Protocol(Code.InvalidInput,
                "Weekly reports become available every Friday afternoon (UTC+8).");
        }
        if (!await HasAccessibleBoardsAsync(userId))
        {
            return this.Protocol(Code.InvalidInput, "No accessible boards are available for this report.");
        }

        var chinaNow = nowUtc + ChinaOffset;
        var weekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(chinaNow);
        if (await db.WeeklyReports.AnyAsync(report =>
                report.UserId == userId && report.WeekStart == weekStart))
        {
            return this.Protocol(Code.NoActionTaken, "This week's report already exists.");
        }

        var user = await userManager.FindByIdAsync(userId);
        var language = DailyReportBackgroundJob.GetLanguageName(user?.DailyReportLanguage ?? "en");
        var cardContext = await WeeklyReportBackgroundJob.BuildWeeklyCardContextAsync(
            db,
            userId,
            weekStart);
        var prompt = cardContext +
                     $"\nGenerate a weekly report for the week of {weekStart:yyyy-MM-dd} " +
                     $"(Monday–Sunday, UTC+8). Current time: {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
                     $"Review the completed cards above and produce a concise bullet-point summary in {language}.";
        string content;
        try
        {
            content = await weeklySubagent.ExecuteAsync(userId, prompt, CancellationToken.None);
        }
        catch
        {
            content = "无法生成本周周报。请稍后再试。\n\n(Weekly report generation failed. Please try again later.)";
        }

        var report = new WeeklyReport
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            WeekStart = weekStart
        };
        db.WeeklyReports.Add(report);
        await db.SaveChangesAsync();
        return this.Protocol(new WeeklyReportResponse
        {
            Code = Code.JobDone,
            Message = "Weekly report generated.",
            Report = ToDto(report)
        });
    }

    [HttpDelete("weekly/{reportId:guid}")]
    public async Task<IActionResult> DeleteWeekly(Guid reportId)
    {
        var userId = CurrentUserId();
        var report = await db.WeeklyReports
            .SingleOrDefaultAsync(item => item.Id == reportId && item.UserId == userId);
        if (report == null)
        {
            return this.Protocol(Code.NotFound, "Weekly report not found.");
        }

        db.WeeklyReports.Remove(report);
        await db.SaveChangesAsync();
        return this.Protocol(Code.JobDone, "Weekly report discarded.");
    }

    private string CurrentUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");

    private async Task<bool> HasAccessibleBoardsAsync(string userId) =>
        await db.KanbanBoards.AnyAsync(board => board.UserId == userId) ||
        await db.BoardShares.AnyAsync(share => share.SharedWithUserId == userId);

    private static DailyReportDto ToDto(DailyReport report) => new()
    {
        Id = report.Id,
        ReportType = report.ReportType.ToString(),
        Content = report.Content,
        Date = report.Date,
        GeneratedAt = report.GeneratedAt
    };

    private static WeeklyReportDto ToDto(WeeklyReport report) => new()
    {
        Id = report.Id,
        Content = report.Content,
        WeekStart = report.WeekStart,
        GeneratedAt = report.GeneratedAt
    };

    private static async Task<(int CurrentPage, int TotalPages, int TotalCount)> PageInfoAsync<T>(
        IQueryable<T> query,
        int requestedPage)
    {
        var totalCount = Math.Min(await query.CountAsync(), MaxDisplayItems);
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / PageSize));
        return (Math.Clamp(requestedPage, 1, totalPages), totalPages, totalCount);
    }
}
