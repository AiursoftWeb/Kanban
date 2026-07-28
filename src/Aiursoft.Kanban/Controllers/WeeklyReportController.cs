using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.WeeklyReportViewModels;
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
public class WeeklyReportController : Controller
{
    private readonly TemplateDbContext _db;
    private readonly WeeklyReportSubagent _weeklySubagent;
    private readonly UserManager<User> _userManager;
    private readonly IAuthorizationService _authorizationService;

    private const int PageSize = 10;
    private const int MaxDisplayItems = 100;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public WeeklyReportController(
        TemplateDbContext db,
        WeeklyReportSubagent weeklySubagent,
        UserManager<User> userManager,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _weeklySubagent = weeklySubagent;
        _userManager = userManager;
        _authorizationService = authorizationService;
    }

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "My Tasks",
        CascadedLinksIcon = "sparkles",
        CascadedLinksOrder = 4,
        LinkText = "Weekly Report",
        LinkOrder = 4)]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = _userManager.GetUserId(User)!;
        var chinaNow = DateTime.UtcNow + ChinaOffset;
        var weekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(chinaNow);
        // Convert back to pure date for display (it's already a UTC-normalized date)
        var displayWeekStart = weekStart.Date;

        var query = _db.WeeklyReports
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.WeekStart)
            .ThenByDescending(r => r.GeneratedAt);

        var totalCount = await query.CountAsync();
        var clampedTotal = Math.Min(totalCount, MaxDisplayItems);
        var totalPages = (int)Math.Ceiling((double)clampedTotal / PageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var reports = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(r => new WeeklyReportItemViewModel
            {
                Id = r.Id,
                Content = r.Content,
                WeekStart = r.WeekStart,
                GeneratedAt = r.GeneratedAt
            })
            .ToListAsync();

        var thisWeekReport = reports.FirstOrDefault(r => r.WeekStart.Date == displayWeekStart);

        // Can generate if: it's Friday afternoon (UTC ≥ 6) and user has accessible boards
        var nowUtc = DateTime.UtcNow;
        var canGenerate = nowUtc.DayOfWeek == DayOfWeek.Friday
            && nowUtc.Hour >= 6
            && thisWeekReport == null
            && await HasAccessibleBoardsAsync(userId);

        return this.StackView(new WeeklyReportIndexViewModel
        {
            Reports = reports,
            ThisWeekReport = thisWeekReport,
            CurrentPage = page,
            TotalPages = totalPages,
            CanGenerate = canGenerate,
            CurrentWeekStart = displayWeekStart
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;
        var report = await _db.WeeklyReports.FindAsync(id);

        if (report == null) return NotFound();
        if (report.UserId != userId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(
                User, AppPermissionNames.CanManageAnyWeeklyReport);
            if (!authResult.Succeeded) return Forbid();
        }

        return this.StackView(new WeeklyReportDetailsViewModel
        {
            Report = report
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate()
    {
        var userId = _userManager.GetUserId(User)!;
        var nowUtc = DateTime.UtcNow;

        // Only allow on Friday afternoon
        if (nowUtc.DayOfWeek != DayOfWeek.Friday || nowUtc.Hour < 6)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!await HasAccessibleBoardsAsync(userId))
        {
            return RedirectToAction(nameof(Index));
        }

        var chinaNow = DateTime.UtcNow + ChinaOffset;
        var weekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(chinaNow);

        // Don't generate if one already exists
        var existing = await _db.WeeklyReports
            .AnyAsync(r => r.UserId == userId && r.WeekStart == weekStart);
        if (existing)
        {
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(userId);
        var language = DailyReportBackgroundJob.GetLanguageName(user?.DailyReportLanguage ?? "en");

        var cardContext = await WeeklyReportBackgroundJob.BuildWeeklyCardContextAsync(_db, userId, weekStart);
        var prompt = cardContext +
                     $"\nGenerate a weekly report for the week of {weekStart:yyyy-MM-dd} (Monday–Sunday, UTC+8). " +
                     $"Current time: {chinaNow:yyyy-MM-dd HH:mm} (UTC+8). " +
                     $"Review the completed cards above and produce a concise bullet-point summary in {language}.";

        string content;
        try
        {
            content = await _weeklySubagent.ExecuteAsync(userId, prompt, CancellationToken.None);
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

        _db.WeeklyReports.Add(report);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(Guid id)
    {
        var userId = _userManager.GetUserId(User)!;
        var report = await _db.WeeklyReports.FindAsync(id);

        if (report == null) return NotFound();
        if (report.UserId != userId)
        {
            var authResult = await _authorizationService.AuthorizeAsync(
                User, AppPermissionNames.CanManageAnyWeeklyReport);
            if (!authResult.Succeeded) return Forbid();
        }

        _db.WeeklyReports.Remove(report);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> HasAccessibleBoardsAsync(string userId)
    {
        var owned = await _db.KanbanBoards
            .AnyAsync(b => b.UserId == userId);

        if (owned) return true;

        var shared = await _db.BoardShares
            .AnyAsync(s => s.SharedWithUserId == userId);

        return shared;
    }
}
