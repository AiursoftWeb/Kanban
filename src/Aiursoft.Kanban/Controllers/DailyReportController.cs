using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.DailyReportViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[LimitPerMin]
[ExcludeFromCodeCoverage]
public class DailyReportController : Controller
{
    private readonly TemplateDbContext _db;
    private readonly DailyPlanningSubagent _planningSubagent;
    private readonly DailySummarySubagent _summarySubagent;
    private readonly UserManager<User> _userManager;
    private readonly IAuthorizationService _authorizationService;

    private const int PageSize = 10;
    private const int MaxDisplayItems = 100;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public DailyReportController(
        TemplateDbContext db,
        DailyPlanningSubagent planningSubagent,
        DailySummarySubagent summarySubagent,
        UserManager<User> userManager,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _planningSubagent = planningSubagent;
        _summarySubagent = summarySubagent;
        _userManager = userManager;
        _authorizationService = authorizationService;
    }

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "Daily Assistant",
        CascadedLinksIcon = "sparkles",
        CascadedLinksOrder = 4,
        LinkText = "Daily Assistant",
        LinkOrder = 11)]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = _userManager.GetUserId(User)!;
        var todayChina = (DateTime.UtcNow + ChinaOffset).Date;
        var chinaNow = DateTime.UtcNow + ChinaOffset;

        var query = _db.DailyReports
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.ReportType);

        var totalCount = await query.CountAsync();
        var clampedTotal = Math.Min(totalCount, MaxDisplayItems);
        var totalPages = (int)Math.Ceiling((double)clampedTotal / PageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var reports = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(r => new DailyReportItemViewModel
            {
                Id = r.Id,
                ReportType = r.ReportType,
                Content = r.Content,
                Date = r.Date,
                GeneratedAt = r.GeneratedAt
            })
            .ToListAsync();

        var todayPlan = reports.FirstOrDefault(r =>
            r.Date.Date == todayChina && r.ReportType == DailyReportType.Plan);
        var todaySummary = reports.FirstOrDefault(r =>
            r.Date.Date == todayChina && r.ReportType == DailyReportType.Summary);

        return this.StackView(new DailyReportIndexViewModel
        {
            Reports = reports,
            TodayPlan = todayPlan,
            TodaySummary = todaySummary,
            CurrentPage = page,
            TotalPages = totalPages,
            CanPlan = chinaNow.Hour < 16,
            CanSummarize = chinaNow.Hour >= 16
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;
        var report = await _db.DailyReports.FindAsync(id);

        if (report == null) return NotFound();
        if (report.UserId != userId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(
                User, AppPermissionNames.CanManageAnyDailyReport);
            if (!authResult.Succeeded) return Forbid();
        }

        return this.StackView(new DailyReportDetailsViewModel
        {
            Report = report
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Regenerate(string type)
    {
        var userId = _userManager.GetUserId(User)!;
        var todayChina = (DateTime.UtcNow + ChinaOffset).Date;
        var chinaNow = DateTime.UtcNow + ChinaOffset;

        DailyReportType reportType;
        if (type == "plan" && chinaNow.Hour < 16)
        {
            reportType = DailyReportType.Plan;
        }
        else if (type == "summary" && chinaNow.Hour >= 16)
        {
            reportType = DailyReportType.Summary;
        }
        else
        {
            return RedirectToAction(nameof(Index));
        }

        // Remove existing report for today
        var existing = await _db.DailyReports
            .Where(r => r.UserId == userId
                     && r.Date == todayChina
                     && r.ReportType == reportType)
            .ToListAsync();

        if (existing.Count > 0)
        {
            _db.DailyReports.RemoveRange(existing);
        }

        var subagent = reportType == DailyReportType.Plan
            ? (ISubagent)_planningSubagent
            : _summarySubagent;

        var cardContext = await DailyReportBackgroundJob.BuildCardContextAsync(_db, _userManager, userId, reportType);
        var prompt = reportType == DailyReportType.Plan
            ? cardContext +
              $"\nGenerate a daily plan for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              "Analyze the data above, then produce a structured plan essay in Chinese."
            : cardContext +
              $"\nGenerate a daily summary for {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
              "Review the data above, then produce a structured summary essay in Chinese.";

        string content;
        try
        {
            content = await subagent.ExecuteAsync(userId, prompt, CancellationToken.None);
        }
        catch
        {
            content = reportType == DailyReportType.Plan
                ? "无法生成今日计划。请稍后再试。\n\n(Plan generation failed. Please try again later.)"
                : "无法生成今日总结。请稍后再试。\n\n(Summary generation failed. Please try again later.)";
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

        return RedirectToAction(nameof(Index));
    }
}
