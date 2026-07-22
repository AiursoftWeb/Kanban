using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.DailyReportViewModels;
using Aiursoft.Kanban.Models.DashboardViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[LimitPerMin]
public class DashboardController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Dashboard",
        CascadedLinksIcon = "layout-dashboard",
        CascadedLinksOrder = 1,
        LinkText = "Overview",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var now = DateTime.UtcNow;

        var ownedBoards = await db.KanbanBoards
            .Where(board => board.UserId == userId)
            .Include(board => board.Columns)
                .ThenInclude(column => column.Cards)
            .OrderBy(board => board.Order)
            .ToListAsync();

        var userRoleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();

        var sharedBoardShares = await db.BoardShares
            .Include(share => share.Board)
                .ThenInclude(board => board.Columns)
                    .ThenInclude(column => column.Cards)
            .Where(share => share.SharedWithUserId == userId ||
                            (share.SharedWithRoleId != null && userRoleIds.Contains(share.SharedWithRoleId)))
            .OrderByDescending(share => share.CreationTime)
            .ToListAsync();

        var sharedBoards = sharedBoardShares
            .Where(share => share.Board.UserId != userId)
            .GroupBy(share => share.BoardId)
            .Select(group =>
            {
                var share = group
                    .OrderByDescending(item => item.Permission)
                    .ThenByDescending(item => item.CreationTime)
                    .First();
                return ToBoardSummary(share.Board, now, share.Permission);
            })
            .OrderBy(board => board.Name)
            .ToList();

        var assignedTasksQuery = db.KanbanCards
            .Include(card => card.CardLabels)
                .ThenInclude(link => link.Label)
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .Where(card => card.AssignedUserId == userId && card.Column.ColumnStatus != ColumnStatus.Completed);

        var assignedTaskCount = await assignedTasksQuery.CountAsync();
        var overdueTaskCount = await assignedTasksQuery
            .CountAsync(card => card.DueDate.HasValue && card.DueDate.Value < now);
        var inProgressTaskCount = await assignedTasksQuery
            .CountAsync(card => card.Column.ColumnStatus == ColumnStatus.InProgress);

        var assignedTasks = await assignedTasksQuery
            .OrderBy(card => card.Priority)
            .ThenBy(card => card.DueDate == null ? 1 : 0)
            .ThenBy(card => card.DueDate)
            .ThenBy(card => card.Title)
            .Take(8)
            .ToListAsync();

        var todayChina = (now + TimeSpan.FromHours(8)).Date;
        var latestPlan = await db.DailyReports
            .Where(r => r.UserId == userId && r.ReportType == DailyReportType.Plan && r.Date == todayChina)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new DailyReportItemViewModel
            {
                Id = r.Id,
                ReportType = r.ReportType,
                Content = r.Content,
                Date = r.Date,
                GeneratedAt = r.GeneratedAt
            })
            .FirstOrDefaultAsync();

        var latestSummary = await db.DailyReports
            .Where(r => r.UserId == userId && r.ReportType == DailyReportType.Summary && r.Date == todayChina)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new DailyReportItemViewModel
            {
                Id = r.Id,
                ReportType = r.ReportType,
                Content = r.Content,
                Date = r.Date,
                GeneratedAt = r.GeneratedAt
            })
            .FirstOrDefaultAsync();

        return this.StackView(new IndexViewModel
        {
            OwnedBoardCount = ownedBoards.Count,
            SharedBoardCount = sharedBoards.Count,
            AssignedTaskCount = assignedTaskCount,
            OverdueTaskCount = overdueTaskCount,
            InProgressTaskCount = inProgressTaskCount,
            AssignedTasks = assignedTasks,
            OwnedBoards = ownedBoards
                .Select(board => ToBoardSummary(board, now))
                .ToList(),
            SharedBoards = sharedBoards,
            LatestPlan = latestPlan,
            LatestSummary = latestSummary
        });
    }

    private static BoardSummaryViewModel ToBoardSummary(
        KanbanBoard board,
        DateTime now,
        SharePermission? permission = null)
    {
        var activeColumns = board.Columns.Where(column => column.ColumnStatus != ColumnStatus.Completed).ToList();
        return new BoardSummaryViewModel
        {
            BoardId = board.Id,
            Name = board.Name,
            TotalCards = board.Columns.Sum(column => column.Cards.Count),
            IncompleteCards = activeColumns.Sum(column => column.Cards.Count),
            InProgressCards = board.Columns
                .Where(column => column.ColumnStatus == ColumnStatus.InProgress)
                .Sum(column => column.Cards.Count),
            CompletedCards = board.Columns
                .Where(column => column.ColumnStatus == ColumnStatus.Completed)
                .Sum(column => column.Cards.Count),
            OverdueCards = activeColumns
                .SelectMany(column => column.Cards)
                .Count(card => card.DueDate.HasValue && card.DueDate.Value < now),
            Permission = permission
        };
    }
}
