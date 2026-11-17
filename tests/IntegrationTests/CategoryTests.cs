using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class CategoryTests : TestBase
{
    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<BoardDto> CreateBoard(string name = "Two dimensions")
    {
        var authentication = await Read<LocalAuthenticationResponse>(await Http.PostAsync("/api/v1/auth/local/login",
            Json(new LocalLoginRequest { EmailOrUserName = "admin@default.com", Password = "Admin@123456!" })));
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        return (await Read<BoardResponse>(await Http.PostAsync("/api/v1/boards", Json(new CreateBoardRequest { Name = name })))).Board;
    }

    private async Task<int> AddCategory(int boardId, string name)
    {
        await LoginAsAdmin();
        var response = await PostForm("/Categories/Create", new() { ["boardId"] = boardId.ToString(), ["name"] = name });
        var json = await Read<JsonElement>(response);
        return json.GetProperty("Id").GetInt32();
    }

    [TestMethod]
    public async Task NewBoardsAndLegacyCardRequestsHaveDefaultCategory()
    {
        var board = await CreateBoard();
        CollectionAssert.AreEqual(new[] { "Todo", "Working", "Done" }, board.Columns.Select(c => c.Name).ToArray());
        Assert.HasCount(1, board.Categories);
        Assert.AreEqual("Default", board.Categories[0].Name);
        Assert.IsTrue(board.Categories[0].IsDefault);
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Legacy client" })))).Card;
        Assert.AreEqual(board.Categories[0].Id, card.CategoryId);
    }

    [TestMethod]
    public async Task CategoryChangesPreserveStatusDatesRecurrenceAndStatusOrder()
    {
        var board = await CreateBoard();
        var categoryId = await AddCategory(board.Id, "Manufacturing");
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[1].Id}/cards",
            Json(new CreateCardRequest { Title = "Pump", CategoryId = categoryId })))).Card;
        var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var entity = await db.KanbanCards.FindAsync(card.Id);
            entity!.ActualStartTime = start;
            entity.ActualEndTime = start.AddHours(1);
            entity.RecurrenceInterval = 2;
            entity.RecurrenceUnit = RecurrenceUnit.Week;
            await db.SaveChangesAsync();
        }
        var response = await PostForm("/Categories/MoveCard", new()
        {
            ["cardId"] = card.Id.ToString(), ["categoryId"] = board.Categories[0].Id.ToString(), ["newOrder"] = "0"
        });
        response.EnsureSuccessStatusCode();
        using var verify = Server!.Services.CreateScope();
        var current = await verify.ServiceProvider.GetRequiredService<TemplateDbContext>().KanbanCards.FindAsync(card.Id);
        Assert.AreEqual(board.Categories[0].Id, current!.CategoryId);
        Assert.AreEqual(card.ColumnId, current.ColumnId);
        Assert.AreEqual(card.Order, current.Order);
        Assert.AreEqual(start, current.ActualStartTime);
        Assert.AreEqual(start.AddHours(1), current.ActualEndTime);
        Assert.AreEqual(card.DueDate, current.DueDate);
        Assert.AreEqual(2, current.RecurrenceInterval);
        Assert.AreEqual(RecurrenceUnit.Week, current.RecurrenceUnit);
    }

    [TestMethod]
    public async Task StatusMoveAndLegacyUpdatePreserveChosenCategory()
    {
        var board = await CreateBoard();
        var categoryId = await AddCategory(board.Id, "Design");
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Drawing", CategoryId = categoryId })))).Card;
        var moved = await Read<CardResponse>(await Http.PutAsync($"/api/v1/cards/{card.Id}/position",
            Json(new MoveCardRequest { TargetColumnId = board.Columns[1].Id, NewOrder = 0 })));
        Assert.AreEqual(categoryId, moved.Card.CategoryId);
        var updated = await Read<CardDetailsResponse>(await Http.PutAsync($"/api/v1/cards/{card.Id}",
            Json(new UpdateCardRequest { Title = "Revised drawing" })));
        Assert.AreEqual(categoryId, updated.Card.CategoryId);
        Assert.AreEqual(board.Columns[1].Id, updated.Card.ColumnId);
    }

    [TestMethod]
    public async Task CrossBoardCategoryIsRejectedWithoutChangingCard()
    {
        var board = await CreateBoard();
        var other = await CreateBoard("Other");
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Original" })))).Card;
        var update = await Http.PutAsync($"/api/v1/cards/{card.Id}",
            Json(new UpdateCardRequest { Title = "Must not save", CategoryId = other.Categories[0].Id }));
        Assert.AreEqual(HttpStatusCode.BadRequest, update.StatusCode);
        await LoginAsAdmin();
        var response = await PostForm("/Categories/MoveCard", new()
        {
            ["cardId"] = card.Id.ToString(), ["categoryId"] = other.Categories[0].Id.ToString(), ["newOrder"] = "0"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var current = (await Read<CardDetailsResponse>(await Http.GetAsync($"/api/v1/cards/{card.Id}"))).Card;
        Assert.AreEqual("Original", current.Title);
        Assert.AreEqual(card.CategoryId, current.CategoryId);
    }

    [TestMethod]
    public async Task CopyPreservesCategoryAndTransferUsesDestinationDefault()
    {
        var board = await CreateBoard();
        var target = await CreateBoard("Destination");
        var categoryId = await AddCategory(board.Id, "Design");
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Drawing", CategoryId = categoryId })))).Card;
        var copy = await Read<CardCopyResponse>(await Http.PostAsync($"/api/v1/cards/{card.Id}/copy", Json(new { })));
        var copied = (await Read<CardDetailsResponse>(await Http.GetAsync($"/api/v1/cards/{copy.CardId}"))).Card;
        Assert.AreEqual(categoryId, copied.CategoryId);
        var transferred = await Read<CardTransferResponse>(await Http.PostAsync($"/api/v1/cards/{card.Id}/transfer",
            Json(new TransferCardRequest { TargetBoardId = target.Id, TargetColumnId = target.Columns[0].Id })));
        var current = (await Read<CardDetailsResponse>(await Http.GetAsync($"/api/v1/cards/{transferred.CardId}"))).Card;
        Assert.AreEqual(target.Categories[0].Id, current.CategoryId);
    }

    [TestMethod]
    public async Task ReadOnlySharesCanSeeCategoriesButCannotCreateRenameOrMoveThem()
    {
        var board = await CreateBoard();
        var categoryId = await AddCategory(board.Id, "Design");
        var card = (await Read<CardResponse>(await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Drawing" })))).Card;
        Http.DefaultRequestHeaders.Authorization = null;
        await LogoutAsync();
        var (email, _) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            db.BoardShares.Add(new BoardShare { BoardId = board.Id, SharedWithUserId = user.Id, Permission = SharePermission.ReadOnly });
            await db.SaveChangesAsync();
        }
        var page = await Http.GetStringAsync($"/Kanban?boardId={board.Id}&groupBy=category");
        Assert.Contains("Design", page);
        foreach (var (url, form) in new (string, Dictionary<string, string>)[]
        {
            ("/Categories/Create", new() { ["boardId"] = board.Id.ToString(), ["name"] = "Denied" }),
            ("/Categories/Rename", new() { ["categoryId"] = categoryId.ToString(), ["name"] = "Denied" }),
            ("/Categories/MoveCard", new() { ["cardId"] = card.Id.ToString(), ["categoryId"] = categoryId.ToString(), ["newOrder"] = "0" }),
            ("/Categories/Delete", new() { ["categoryId"] = categoryId.ToString() })
        })
        {
            var response = await PostForm(url, form);
            AssertRedirect(response, "/Error/Code403", exact: false);
        }
    }

    [TestMethod]
    public async Task DefaultAndPopulatedCategoriesCannotBeDeletedAndArchivedBoardsCannotChange()
    {
        var board = await CreateBoard();
        var categoryId = await AddCategory(board.Id, "Design");
        await Http.PostAsync($"/api/v1/columns/{board.Columns[0].Id}/cards",
            Json(new CreateCardRequest { Title = "Drawing", CategoryId = categoryId }));
        foreach (var id in new[] { board.Categories[0].Id, categoryId })
        {
            var deleted = await PostForm("/Categories/Delete", new() { ["categoryId"] = id.ToString() });
            Assert.AreEqual(HttpStatusCode.BadRequest, deleted.StatusCode);
        }
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.KanbanBoards.FindAsync(board.Id))!.IsArchived = true;
            await db.SaveChangesAsync();
        }
        var rename = await PostForm("/Categories/Rename", new() { ["categoryId"] = categoryId.ToString(), ["name"] = "Changed" });
        AssertRedirect(rename, "/Error/Code403", exact: false);
    }
}
