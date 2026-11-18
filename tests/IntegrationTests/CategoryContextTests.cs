using System.Reflection;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class CategoryContextTests : TestBase
{
    [TestMethod]
    public async Task CardContexts_IncludeCategoriesAndRulesWithCustomPrompt()
    {
        await LoginAsAdmin();
        string userId;
        int boardId;
        int categoryId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == "admin@default.com");
            userId = user.Id;
            var board = new KanbanBoard { Name = "Context board", UserId = userId };
            db.KanbanBoards.Add(board);
            await db.SaveChangesAsync();
            boardId = board.Id;
            var category = await db.KanbanCategories.SingleAsync(c => c.BoardId == boardId && c.IsDefault);
            category.Name = "Context category";
            categoryId = category.Id;
            var active = new KanbanColumn { Name = "Active", BoardId = boardId };
            var completed = new KanbanColumn { Name = "Done", BoardId = boardId, ColumnStatus = ColumnStatus.Completed };
            db.KanbanColumns.AddRange(active, completed);
            db.KanbanCards.AddRange(
                new KanbanCard { Title = "Overdue context card", Column = active, Category = category, AssignedUserId = userId, DueDate = DateTime.UtcNow.Date.AddDays(-1) },
                new KanbanCard { Title = "Completed context card", Column = completed, Category = category, AssignedUserId = userId, ActualEndTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var readScope = Server!.Services.CreateScope();
        var services = readScope.ServiceProvider;
        var agent = (AgentService)services.GetRequiredService<IAgentService>();
        var create = typeof(AgentService).GetMethod("CreateConversation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var conversation = await (Task<AgentConversation>)create.Invoke(agent, [userId, boardId, "hello", null, "Custom prompt"])!;
        Assert.AreEqual("Custom prompt", conversation.Messages[0].Content);
        var reminder = conversation.Messages.Single(m => m.Content != null && m.Content.Contains("Categories are distinct"));
        StringAssert.Contains(reminder.Content!, "Every card must have a category belonging to the same board");
        var recent = conversation.Messages.Single(m => m.Content != null && m.Content.Contains("Recently active cards"));
        var assigned = conversation.Messages.Single(m => m.Content != null && m.Content.Contains("Cards assigned to you"));
        foreach (var message in new[] { recent.Content!, assigned.Content! })
        {
            StringAssert.Contains(message, $"Category: \"Context category\" (ID: {categoryId})");
        }

        var dbRead = services.GetRequiredService<TemplateDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var build = typeof(DailyReportBackgroundJob).GetMethod("BuildCardContextAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var context = await (Task<string>)build.Invoke(null, [dbRead, userManager, userId, DailyReportType.Summary])!;
        var cardLines = context.Split('\n').Where(line => line.Contains("context card")).ToArray();
        Assert.AreEqual(3, cardLines.Length); // 未完成、逾期和今日完成
        foreach (var line in cardLines)
        {
            StringAssert.Contains(line, $"Category: Context category, ID: {categoryId}");
        }

        var weeklyBuild = typeof(WeeklyReportBackgroundJob).GetMethod("BuildWeeklyCardContextAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var weeklyContext = await (Task<string>)weeklyBuild.Invoke(null, [dbRead, userId, DateTime.UtcNow.Date])!;
        StringAssert.Contains(weeklyContext, $"Category: Context category, ID: {categoryId}");
    }
}
