using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/dashboard")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class DashboardApiController(
    TemplateDbContext db,
    UserManager<User> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Overview()
    {
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");
        var now = DateTime.UtcNow;
        var ownedBoards = await db.KanbanBoards
            .Where(board => board.UserId == userId)
            .Include(board => board.Columns)
                .ThenInclude(column => column.Cards)
            .OrderBy(board => board.Order)
            .ToListAsync();
        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        var sharedBoardShares = await db.BoardShares
            .Include(share => share.Board)
                .ThenInclude(board => board.Columns)
                    .ThenInclude(column => column.Cards)
            .Where(share => share.SharedWithUserId == userId ||
                (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId)))
            .OrderByDescending(share => share.CreationTime)
            .ToListAsync();
        var sharedBoards = sharedBoardShares
            .Where(share => share.Board.UserId != userId)
            .GroupBy(share => share.BoardId)
            .Select(group => group
                .OrderByDescending(item => item.Permission)
                .ThenByDescending(item => item.CreationTime)
                .First())
            .OrderBy(share => share.Board.Name)
            .Select(share => ToBoardDto(share.Board, now, share.Permission))
            .ToList();

        var assignedTasksQuery = db.KanbanCards
            .Include(card => card.CardLabels)
                .ThenInclude(link => link.Label)
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .Include(card => card.AssignedUser)
            .Where(card => card.AssignedUserId == userId &&
                card.Column.ColumnStatus != ColumnStatus.Completed);
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
        var latestPlan = await LatestDailyReportAsync(userId, todayChina, DailyReportType.Plan);
        var latestSummary = await LatestDailyReportAsync(userId, todayChina, DailyReportType.Summary);

        return this.Protocol(new DashboardResponse
        {
            Code = Code.ResultShown,
            Message = "Kanban dashboard.",
            OwnedBoardCount = ownedBoards.Count,
            SharedBoardCount = sharedBoards.Count,
            AssignedTaskCount = assignedTaskCount,
            OverdueTaskCount = overdueTaskCount,
            InProgressTaskCount = inProgressTaskCount,
            AssignedTasks = assignedTasks.Select(MobileApiMapper.ToTaskDto).ToList(),
            OwnedBoards = ownedBoards.Select(board => ToBoardDto(board, now)).ToList(),
            SharedBoards = sharedBoards,
            LatestPlan = latestPlan,
            LatestSummary = latestSummary
        });
    }

    private async Task<DailyReportDto?> LatestDailyReportAsync(
        string userId,
        DateTime date,
        DailyReportType reportType) =>
        await db.DailyReports
            .Where(report => report.UserId == userId &&
                report.ReportType == reportType &&
                report.Date == date)
            .OrderByDescending(report => report.GeneratedAt)
            .Select(report => new DailyReportDto
            {
                Id = report.Id,
                ReportType = report.ReportType.ToString(),
                Content = report.Content,
                Date = report.Date,
                GeneratedAt = report.GeneratedAt
            })
            .FirstOrDefaultAsync();

    private static DashboardBoardDto ToBoardDto(
        KanbanBoard board,
        DateTime now,
        SharePermission? permission = null)
    {
        var activeColumns = board.Columns
            .Where(column => column.ColumnStatus != ColumnStatus.Completed)
            .ToList();
        return new DashboardBoardDto
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
            Permission = permission?.ToString()
        };
    }
}
