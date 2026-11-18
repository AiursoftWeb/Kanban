using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Tools.Read;
using Aiursoft.Kanban.Services.Tools.Write;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class CategoryAgentToolTests : TestBase
{
    private static async Task<(KanbanBoard Board, KanbanColumn Column, KanbanCategory Category)> CreateBoardAsync(
        TemplateDbContext db, string userId, string name = "Category tools board")
    {
        var board = new KanbanBoard { Name = name, UserId = userId };
        var column = new KanbanColumn { Name = "Working", Board = board, ColumnStatus = ColumnStatus.InProgress };
        var category = new KanbanCategory { Name = "Design", Board = board, Order = 1 };
        db.KanbanBoards.Add(board);
        db.KanbanColumns.Add(column);
        db.KanbanCategories.Add(category);
        await db.SaveChangesAsync();
        return (board, column, category);
    }

    private static async Task<string> SetAdminAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<TemplateDbContext>();
        var userId = await db.Users.Where(u => u.Email == "admin@default.com").Select(u => u.Id).SingleAsync();
        services.GetRequiredService<CurrentUserService>().UserId = userId;
        return userId;
    }

    [TestMethod]
    public async Task CategoryTools_AreRegisteredWithWriteApprovalAndOptionalSchemas()
    {
        using var scope = Server!.Services.CreateScope();
        await SetAdminAsync(scope.ServiceProvider);
        var registry = scope.ServiceProvider.GetRequiredService<ToolRegistry>();
        Assert.IsNotNull(registry.GetTool("GetCategories"));
        Assert.IsFalse(registry.IsWriteTool("GetCategories"));
        foreach (var name in new[] { "CreateCategory", "RenameCategory", "DeleteCategory", "SetCardCategory" })
        {
            Assert.IsNotNull(registry.GetTool(name));
            Assert.IsTrue(registry.IsWriteTool(name));
        }
        foreach (var name in new[] { "CreateCard", "FilterCards" })
        {
            var schema = registry.GetTool(name)!.ProtocolTool.InputSchema;
            Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("categoryId", out _));
            if (schema.TryGetProperty("required", out var required))
                Assert.IsFalse(required.EnumerateArray().Any(p => p.GetString() == "categoryId"));
        }
    }

    [TestMethod]
    public async Task CategoryTools_CreateRenameDelete_EnforceValidationAndDeletionRules()
    {
        using var scope = Server!.Services.CreateScope();
        var services = scope.ServiceProvider;
        var userId = await SetAdminAsync(services);
        var db = services.GetRequiredService<TemplateDbContext>();
        var (board, column, category) = await CreateBoardAsync(db, userId);
        var tools = services.GetRequiredService<CategoryWriteTools>();
        StringAssert.Contains(await tools.CreateCategory(board.Id, "  Research  "), "Category created:");
        var created = await db.KanbanCategories.SingleAsync(c => c.BoardId == board.Id && c.Name == "Research");
        StringAssert.Contains(await tools.CreateCategory(board.Id, "Research"), "Error:");
        StringAssert.Contains(await tools.CreateCategory(board.Id, " "), "Error:");
        StringAssert.Contains(await tools.CreateCategory(board.Id, new string('x', 101)), "Error:");
        StringAssert.Contains(await tools.RenameCategory(created.Id, " Design "), "Error:");
        StringAssert.Contains(await tools.RenameCategory(created.Id, " "), "Error:");
        StringAssert.Contains(await tools.RenameCategory(created.Id, new string('x', 101)), "Error:");
        StringAssert.Contains(await tools.RenameCategory(created.Id, "  Architecture  "), "renamed");
        Assert.AreEqual("Architecture", created.Name);
        var defaultCategory = await db.KanbanCategories.SingleAsync(c => c.BoardId == board.Id && c.IsDefault);
        StringAssert.Contains(await tools.DeleteCategory(defaultCategory.Id), "Error:");
        db.KanbanCards.Add(new KanbanCard { Title = "Keep this card", Column = column, Category = category });
        await db.SaveChangesAsync();
        StringAssert.Contains(await tools.DeleteCategory(category.Id), "Error:");
        Assert.AreEqual(1, await db.KanbanCards.CountAsync(c => c.ColumnId == column.Id));
        StringAssert.Contains(await tools.DeleteCategory(created.Id), "deleted");
        Assert.IsFalse(await db.KanbanCategories.AnyAsync(c => c.Id == created.Id));
    }

    [TestMethod]
    public async Task CategoryTools_SetCategory_PreservesStatusTimesAndRejectsCrossBoard()
    {
        using var scope = Server!.Services.CreateScope();
        var services = scope.ServiceProvider;
        var userId = await SetAdminAsync(services);
        var db = services.GetRequiredService<TemplateDbContext>();
        var (board, column, category) = await CreateBoardAsync(db, userId);
        var (_, _, otherCategory) = await CreateBoardAsync(db, userId, "Other board");
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var card = new KanbanCard
        {
            Title = "Move category only", Column = column, Order = 7,
            ActualStartTime = start, ActualEndTime = start.AddHours(1), DueDate = start.AddDays(2),
            RecurrenceInterval = 2, RecurrenceUnit = RecurrenceUnit.Week
        };
        db.KanbanCards.Add(card);
        db.KanbanCards.Add(new KanbanCard { Title = "Existing category card", Column = column, Category = category });
        await db.SaveChangesAsync();
        var originalCategoryId = card.CategoryId;
        var tools = services.GetRequiredService<CategoryWriteTools>();
        StringAssert.Contains(await tools.SetCardCategory(card.Id, otherCategory.Id), "Error:");
        Assert.AreEqual(originalCategoryId, card.CategoryId);
        StringAssert.Contains(await tools.SetCardCategory(card.Id, category.Id), $"category #{category.Id}");
        Assert.AreEqual(category.Id, card.CategoryId);
        Assert.AreEqual(1, card.CategoryOrder);
        Assert.AreEqual(column.Id, card.ColumnId);
        Assert.AreEqual(ColumnStatus.InProgress, column.ColumnStatus);
        Assert.AreEqual(7, card.Order);
        Assert.AreEqual(start, card.ActualStartTime);
        Assert.AreEqual(start.AddHours(1), card.ActualEndTime);
        Assert.AreEqual(start.AddDays(2), card.DueDate);
        Assert.AreEqual(2, card.RecurrenceInterval);
        Assert.AreEqual(RecurrenceUnit.Week, card.RecurrenceUnit);
        var defaultId = await db.KanbanCategories.Where(c => c.BoardId == board.Id && c.IsDefault).Select(c => c.Id).SingleAsync();
        await tools.SetCardCategory(card.Id, defaultId);
        Assert.AreEqual(defaultId, card.CategoryId);
    }

    [TestMethod]
    public async Task CategoryTools_CreateSingleAndBatchCards_SupportCategoryAndLegacyDefaults()
    {
        using var scope = Server!.Services.CreateScope();
        var services = scope.ServiceProvider;
        var userId = await SetAdminAsync(services);
        var db = services.GetRequiredService<TemplateDbContext>();
        var (board, column, category) = await CreateBoardAsync(db, userId);
        var (_, _, otherCategory) = await CreateBoardAsync(db, userId, "Other board");
        var single = services.GetRequiredService<CardWriteTools>();
        StringAssert.Contains(await single.CreateCard(column.Id, "Selected", null, categoryId: category.Id), "Card created:");
        StringAssert.Contains(await single.CreateCard(column.Id, "Legacy", null), "Card created:");
        StringAssert.Contains(await single.CreateCard(column.Id, "Rejected", null, categoryId: otherCategory.Id), "Error:");
        var batch = services.GetRequiredService<BatchWriteTools>();
        var invalidBatch = "[{\"title\":\"Atomic valid\",\"categoryId\":" + category.Id + "},{\"title\":\"Atomic invalid\",\"categoryId\":" + otherCategory.Id + "}]";
        StringAssert.Contains(await batch.BatchCreateCards(column.Id, invalidBatch), "Error:");
        Assert.IsFalse(await db.KanbanCards.AnyAsync(c => c.Title.StartsWith("Atomic")));
        var validBatch = "[{\"title\":\"Batch selected\",\"categoryId\":" + category.Id + "},{\"title\":\"Batch legacy\"}]";
        StringAssert.Contains(await batch.BatchCreateCards(column.Id, validBatch), "Created 2");
        var cards = await db.KanbanCards.Where(c => c.ColumnId == column.Id).ToListAsync();
        Assert.HasCount(4, cards);
        var defaultId = await db.KanbanCategories.Where(c => c.BoardId == board.Id && c.IsDefault).Select(c => c.Id).SingleAsync();
        foreach (var card in cards)
            Assert.AreEqual(card.Title.Contains("selected", StringComparison.OrdinalIgnoreCase) ? category.Id : defaultId, card.CategoryId);
    }

    [TestMethod]
    public async Task CategoryTools_ReadOutputsAndFilter_IncludeCategoriesFromFreshScope()
    {
        int boardId, columnId, categoryId, cardId, otherCategoryId;
        using (var scope = Server!.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var userId = await SetAdminAsync(services);
            var db = services.GetRequiredService<TemplateDbContext>();
            var (board, column, category) = await CreateBoardAsync(db, userId);
            var (_, _, otherCategory) = await CreateBoardAsync(db, userId, "Other board");
            var card = new KanbanCard { Title = "Selected card", Column = column, Category = category, AssignedUserId = userId, DueDate = DateTime.UtcNow.AddDays(-1) };
            db.KanbanCards.Add(card);
            db.KanbanCards.Add(new KanbanCard { Title = "Default card", Column = column, AssignedUserId = userId });
            await db.SaveChangesAsync();
            (boardId, columnId, categoryId, cardId, otherCategoryId) = (board.Id, column.Id, category.Id, card.Id, otherCategory.Id);
        }
        using var freshScope = Server!.Services.CreateScope();
        var fresh = freshScope.ServiceProvider;
        await SetAdminAsync(fresh);
        var cards = fresh.GetRequiredService<CardReadTools>();
        foreach (var output in new[]
        {
            await cards.GetCardById(cardId), await cards.SearchCards("Selected card"),
            await cards.GetMyTasks(), await cards.GetOverdueCards(boardId),
            await cards.GetCardsByPriority(boardId, "None"),
            await cards.GetCardsByDateRange("2026-01-01", "2027-01-01", dateType: "created"),
            await fresh.GetRequiredService<BoardReadTools>().GetBoardById(boardId),
            await fresh.GetRequiredService<ColumnReadTools>().GetCardsInColumn(columnId)
        })
        {
            StringAssert.Contains(output, $"Category: \"Design\" (#{categoryId})");
        }
        var categoryOutput = await fresh.GetRequiredService<CategoryReadTools>().GetCategories(boardId);
        StringAssert.Contains(categoryOutput, "Design");
        StringAssert.Contains(categoryOutput, "IsDefault: True");
        var filtered = await cards.FilterCards(boardId: boardId, categoryId: categoryId);
        StringAssert.Contains(filtered, "Selected card");
        Assert.IsFalse(filtered.Contains("Default card"));
        StringAssert.Contains(await cards.FilterCards(boardId: boardId, categoryId: otherCategoryId), "Error:");
    }

    [TestMethod]
    public async Task CategoryTools_EnforceReadOnlyPrivateAndArchivedPermissions()
    {
        using var scope = Server!.Services.CreateScope();
        var services = scope.ServiceProvider;
        var userId = await SetAdminAsync(services);
        var db = services.GetRequiredService<TemplateDbContext>();
        var (board, column, category) = await CreateBoardAsync(db, userId);
        var card = new KanbanCard { Title = "Protected card", Column = column, Category = category };
        var viewer = new User { Id = Guid.NewGuid().ToString(), UserName = "category-viewer", DisplayName = "Category viewer" };
        db.Users.Add(viewer);
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        services.GetRequiredService<CurrentUserService>().UserId = viewer.Id;
        var read = services.GetRequiredService<CategoryReadTools>();
        var write = services.GetRequiredService<CategoryWriteTools>();
        StringAssert.Contains(await read.GetCategories(board.Id), "Error:");
        StringAssert.Contains(await services.GetRequiredService<CardReadTools>().FilterCards(categoryId: category.Id), "Error:");
        db.BoardShares.Add(new BoardShare { BoardId = board.Id, SharedWithUserId = viewer.Id, Permission = SharePermission.ReadOnly });
        await db.SaveChangesAsync();
        StringAssert.Contains(await read.GetCategories(board.Id), "Design");
        StringAssert.Contains(await write.CreateCategory(board.Id, "Denied"), "Error:");
        StringAssert.Contains(await write.RenameCategory(category.Id, "Denied"), "Error:");
        StringAssert.Contains(await write.DeleteCategory(category.Id), "Error:");
        StringAssert.Contains(await write.SetCardCategory(card.Id, category.Id), "Error:");
        services.GetRequiredService<CurrentUserService>().UserId = userId;
        board.IsArchived = true;
        await db.SaveChangesAsync();
        StringAssert.Contains(await write.CreateCategory(board.Id, "Denied"), "Error:");
        StringAssert.Contains(await write.RenameCategory(category.Id, "Denied"), "Error:");
        StringAssert.Contains(await write.DeleteCategory(category.Id), "Error:");
        StringAssert.Contains(await write.SetCardCategory(card.Id, category.Id), "Error:");
        Assert.AreEqual("Design", category.Name);
    }
}
