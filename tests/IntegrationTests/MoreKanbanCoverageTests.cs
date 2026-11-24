using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Notifications;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Aiursoft.Kanban.Services.Tools.Read;
using Aiursoft.Kanban.Services.Tools.Write;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class MoreKanbanCoverageTests : TestBase
{
    private async Task<int> CreateBoardAndGetIdAsync(string name)
    {
        var response = await PostForm("/Kanban/CreateBoard", new Dictionary<string, string>
        {
            { "name", name }
        });
        var location = response.Headers.Location!.OriginalString;
        return int.Parse(location.Split("boardId=")[1].Split('&')[0]);
    }

    [TestMethod]
    public void TestNotificationTemplates()
    {
        var dict = new Dictionary<string, string>
        {
            ["ActorName"] = "Alice",
            ["CardTitle"] = "Task A",
            ["ColumnName"] = "Done",
            ["BoardName"] = "Project",
            ["ChangedFields"] = "title"
        };

        foreach (NotificationType t in Enum.GetValues<NotificationType>())
        {
            var msg = NotificationTemplateService.BuildMessage(t, dict);
            Assert.IsNotNull(msg);
        }
    }

    [TestMethod]
    public async Task TestKanbanAdditionalViewsAndTools()
    {
        await LoginAsAdmin();
        var boardId = await CreateBoardAndGetIdAsync("Test Board More");

        // 1. Visit Archived, ManageShares, ManageBoard, Notifications
        var archivedResp = await Http.GetAsync($"/Kanban/Archived/{boardId}");
        Assert.IsTrue(archivedResp.IsSuccessStatusCode);

        var manageSharesResp = await Http.GetAsync($"/Kanban/ManageShares/{boardId}");
        Assert.IsTrue(manageSharesResp.IsSuccessStatusCode);

        var manageBoardResp = await Http.GetAsync($"/Kanban/ManageBoard/{boardId}");
        Assert.IsTrue(manageBoardResp.IsSuccessStatusCode);

        var notifResp = await Http.GetAsync("/Notifications");
        Assert.IsTrue(notifResp.IsSuccessStatusCode);

        // 2. Search
        var searchResp = await Http.GetAsync("/Search?q=Test");
        Assert.IsTrue(searchResp.IsSuccessStatusCode);

        // 3. MyTasks variations
        var myTasksUrls = new[]
        {
            "/MyTasks",
            "/MyTasks?status=incomplete",
            "/MyTasks?status=completed",
            "/MyTasks?status=all",
            "/MyTasks?sort=due-date-asc",
            "/MyTasks?sort=priority-desc",
            "/MyTasks?sort=title-asc"
        };
        foreach (var url in myTasksUrls)
        {
            var myTasksResp = await Http.GetAsync(url);
            Assert.IsTrue(myTasksResp.IsSuccessStatusCode, $"Failed GET on {url}");
        }

        // 4. Background job & BoardReadTools & Write Tools
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        await settings.UpdateSettingAsync(SettingsMap.AutoSetPlannedStartTime, "True");
        await settings.UpdateSettingAsync(SettingsMap.PlannedStartTimeAdvanceDays, "3");

        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var column = await db.KanbanColumns.FirstAsync(c => c.BoardId == boardId);
        db.KanbanCards.Add(new KanbanCard
        {
            ColumnId = column.Id,
            Title = "Auto Start Test Card",
            DueDate = DateTime.UtcNow.AddDays(7),
            PlannedStartTime = null
        });
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<AutoSetPlannedStartTimeBackgroundJob>();
        await job.ExecuteAsync();

        var updatedCard = await db.KanbanCards.FirstAsync(c => c.Title == "Auto Start Test Card");
        Assert.IsNotNull(updatedCard.PlannedStartTime);

        var adminUser = await db.Users.FirstAsync();
        var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUserService>();
        currentUser.UserId = adminUser.Id;

        var boardTools = scope.ServiceProvider.GetRequiredService<BoardReadTools>();
        var userBoards = await boardTools.GetUserBoards();
        Assert.IsNotNull(userBoards);
        var boardInfo = await boardTools.GetBoardById(boardId);
        Assert.IsNotNull(boardInfo);
        var boards = await boardTools.GetBoards();
        Assert.IsNotNull(boards);

        var columnTools = scope.ServiceProvider.GetRequiredService<ColumnReadTools>();
        var boardColumns = await columnTools.GetColumns(boardId);
        Assert.IsNotNull(boardColumns);
        var columnInfo = await columnTools.GetColumnById(column.Id);
        Assert.IsNotNull(columnInfo);
        var cardsInCol = await columnTools.GetCardsInColumn(column.Id);
        Assert.IsNotNull(cardsInCol);

        var columnWriteTools = scope.ServiceProvider.GetRequiredService<ColumnWriteTools>();
        var createdColResult = await columnWriteTools.CreateColumn(boardId, "New Extra Col");
        Assert.IsTrue(createdColResult.Contains("Column created"));
        var extraCol = await db.KanbanColumns.FirstAsync(c => c.Name == "New Extra Col");
        await columnWriteTools.RenameColumn(extraCol.Id, "Renamed Extra Col");

        var cardTools = scope.ServiceProvider.GetRequiredService<CardWriteTools>();
        var createResult = await cardTools.CreateCard(column.Id, "Tool Created Card", "Tool description");
        Assert.IsTrue(createResult.Contains("Card created"));

        var cardInDb = await db.KanbanCards.FirstAsync(c => c.Title == "Tool Created Card");
        await cardTools.UpdateCardDetails(cardInDb.Id, "Updated Tool Card", "New description", null, null, 2, adminUser.Id);

        var labelTools = scope.ServiceProvider.GetRequiredService<LabelWriteTools>();
        var labelResult = await labelTools.AddLabel(cardInDb.Id, "Urgent");
        Assert.IsNotNull(labelResult);

        var cardReadTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();
        var cardDetail = await cardReadTools.GetCardById(cardInDb.Id);
        Assert.IsNotNull(cardDetail);
        var searchCardsResult = await cardReadTools.SearchCards("Tool");
        Assert.IsNotNull(searchCardsResult);

        var userLookupTools = scope.ServiceProvider.GetRequiredService<UserLookupTools>();
        var members = await userLookupTools.GetBoardMembers(boardId);
        Assert.IsNotNull(members);
        var shares = await userLookupTools.GetBoardShares(boardId);
        Assert.IsNotNull(shares);

        var categoryTools = scope.ServiceProvider.GetRequiredService<CategoryReadTools>();
        var categories = await categoryTools.GetCategories(boardId);
        Assert.IsNotNull(categories);

        var categoryWriteTools = scope.ServiceProvider.GetRequiredService<CategoryWriteTools>();
        var createCatResult = await categoryWriteTools.CreateCategory(boardId, "Tool Category");
        Assert.IsTrue(createCatResult.Contains("Category created"));
        var newCat = await db.KanbanCategories.FirstAsync(c => c.Name == "Tool Category");
        await categoryWriteTools.RenameCategory(newCat.Id, "Renamed Category");
        await categoryWriteTools.SetCardCategory(cardInDb.Id, newCat.Id);

        var labelReadTools = scope.ServiceProvider.GetRequiredService<LabelReadTools>();
        var searchLabels = await labelReadTools.SearchLabels("");
        Assert.IsNotNull(searchLabels);
        var cardLabels = await labelReadTools.GetLabelsForCard(cardInDb.Id);
        Assert.IsNotNull(cardLabels);

        var notifTools = scope.ServiceProvider.GetRequiredService<NotificationReadTools>();
        var unreadCount = await notifTools.GetUnreadNotificationCount();
        Assert.IsNotNull(unreadCount);
        var unreadList = await notifTools.GetUnreadNotifications();
        Assert.IsNotNull(unreadList);

        var boardWriteTools = scope.ServiceProvider.GetRequiredService<BoardWriteTools>();
        var createBoardToolResult = await boardWriteTools.CreateBoard("Tool Board");
        Assert.IsTrue(createBoardToolResult.Contains("Board created"));
        var toolBoard = await db.KanbanBoards.FirstAsync(b => b.Name == "Tool Board");
        await boardWriteTools.RenameBoard(toolBoard.Id, "Renamed Tool Board");
        await boardWriteTools.DeleteBoard(toolBoard.Id);

        var shareWriteTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();
        await shareWriteTools.UpdateBoardVisibility(boardId, true);
        await shareWriteTools.UpdateBoardVisibility(boardId, false);

        var columns = await db.KanbanColumns.Where(c => c.BoardId == boardId).ToListAsync();
        if (columns.Count > 1)
        {
            var moveCardResult = await cardTools.MoveCard(cardInDb.Id, columns[1].Id, 0);
            Assert.IsNotNull(moveCardResult);

            var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
            var batchCreateResult = await batchTools.BatchCreateCards(columns[0].Id, "[{\"title\":\"Batch A\"},{\"title\":\"Batch B\"}]");
            Assert.IsTrue(batchCreateResult.Contains("Created"));

            var batchMoveResult = await batchTools.BatchMoveCards($"[{cardInDb.Id}]", columns[0].Id);
            Assert.IsNotNull(batchMoveResult);
        }

        // Test AddShare post
        var shareResponse = await PostForm($"/Kanban/AddShare/{boardId}", new Dictionary<string, string>
        {
            { "TargetUserId", adminUser.Id },
            { "Level", "0" }
        });
        Assert.IsTrue(shareResponse.StatusCode is System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.OK);
    }
}
