using System.Net;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class KanbanControllerTests : TestBase
{
    // ── Index ──────────────────────────────────────────────

    [TestMethod]
    public async Task Index_NoBoards_ReturnsEmptyPage()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Kanban/Index");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No boards yet", html);
    }

    [TestMethod]
    public async Task Index_AfterCreatingBoard_ShowsBoardName()
    {
        await LoginAsAdmin();
        await CreateBoardAsync("Test Board");

        var response = await Http.GetAsync("/Kanban/Index");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Test Board", html);
    }

    [TestMethod]
    public async Task Index_Unauthenticated_RedirectsToLogin()
    {
        var response = await Http.GetAsync("/Kanban/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        StringAssert.Contains(response.Headers.Location!.OriginalString, "Login");
    }

    // ── CreateBoard ────────────────────────────────────────

    [TestMethod]
    public async Task CreateBoard_ValidName_CreatesBoardWithDefaultColumns()
    {
        await LoginAsAdmin();
        var response = await CreateBoardAsync("My Board");

        AssertRedirect(response, "/Kanban?boardId=", exact: false);
        var indexResponse = await Http.GetAsync(response.Headers.Location!);
        var html = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("My Board", html);
        Assert.Contains("To Do", html);
        Assert.Contains("In Progress", html);
        Assert.Contains("Done", html);
    }

    [TestMethod]
    public async Task CreateBoard_EmptyName_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var response = await CreateBoardAsync("   ");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── CreateColumn ───────────────────────────────────────

    [TestMethod]
    public async Task CreateColumn_ValidName_ReturnsJsonWithColumnData()
    {
        await LoginAsAdmin();
        var boardId = await CreateBoardAndGetIdAsync("Board");

        var response = await Http.PostAsync(
            $"/Kanban/CreateColumn?boardId={boardId}&name=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("Review", doc.RootElement.GetProperty("Name").GetString());
        Assert.AreEqual(3, doc.RootElement.GetProperty("Order").GetInt32()); // after 0,1,2
    }

    [TestMethod]
    public async Task CreateColumn_EmptyName_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var response = await Http.PostAsync(
            "/Kanban/CreateColumn?boardId=1&name=",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── CreateCard ─────────────────────────────────────────

    [TestMethod]
    public async Task CreateCard_ValidTitle_ReturnsJsonWithCardData()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        var response = await PostAsync(
            $"/Kanban/CreateCard?columnId={columnId}&title=Setup CI",
            new Dictionary<string, string> { { "description", "Add GitHub Actions" } });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("Setup CI", doc.RootElement.GetProperty("Title").GetString());
        Assert.AreEqual("Add GitHub Actions", doc.RootElement.GetProperty("Description").GetString());
    }

    [TestMethod]
    public async Task CreateCard_EmptyTitle_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        await CreateBoardAndGetIdAsync("Board");
        var response = await PostAsync(
            $"/Kanban/CreateCard?columnId=1&title=   ",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateCard_NonExistentColumn_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await PostAsync(
            "/Kanban/CreateCard?columnId=9999&title=Ghost",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DeleteCard ─────────────────────────────────────────

    [TestMethod]
    public async Task DeleteCard_WithRelations_DeletesSuccessfully()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Delete me");

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var cardEntity = await db.KanbanCards.FindAsync(card.Id);
            var authorId = await db.Users
                .Where(user => user.Email == "admin@default.com")
                .Select(user => user.Id)
                .SingleAsync();
            var label = new KanbanLabel { Name = "Delete", Color = "#EF4444" };
            db.KanbanCardLabels.Add(new KanbanCardLabel { Card = cardEntity!, Label = label });
            db.KanbanCardComments.Add(new KanbanCardComment
            {
                Card = cardEntity!,
                AuthorId = authorId,
                Content = "Delete with card"
            });
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(
            $"/Kanban/DeleteCard?cardId={card.Id}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await verificationDb.KanbanCards.FindAsync(card.Id));
        Assert.IsFalse(await verificationDb.KanbanCardLabels.AnyAsync(link => link.CardId == card.Id));
        Assert.IsFalse(await verificationDb.KanbanCardComments.AnyAsync(comment => comment.CardId == card.Id));
    }

    [TestMethod]
    public async Task DeleteCard_NonExistent_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await PostAsync(
            "/Kanban/DeleteCard?cardId=9999",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── MoveCard ───────────────────────────────────────────

    [TestMethod]
    public async Task MoveCard_ToDifferentColumn_ReturnsOk()
    {
        await LoginAsAdmin();
        var (boardId, sourceColId) = await CreateBoardAndFirstColumnAsync();
        var destColId = sourceColId + 1;

        var card = await CreateCardAndGetIdAsync(sourceColId, "Drag me");

        var response = await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={destColId}&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var page = await Http.GetAsync($"/Kanban/Index?boardId={boardId}");
        var html = await page.Content.ReadAsStringAsync();

        Assert.IsTrue(
            html.IndexOf("To Do", StringComparison.Ordinal) < html.IndexOf("In Progress", StringComparison.Ordinal),
            "Source column should appear before dest column in page order");
    }

    [TestMethod]
    public async Task MoveCard_NonExistentCard_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await PostAsync(
            "/Kanban/MoveCard?cardId=9999&targetColumnId=1&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MoveCard_NonExistentTargetColumn_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Card");

        var response = await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId=9999&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MoveCard_ToColumnInDifferentBoard_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var (_, sourceColumnId) = await CreateBoardAndFirstColumnAsync();
        var targetBoardId = await CreateBoardAndGetIdAsync("Target Board");
        int targetColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            targetColumnId = db.KanbanColumns.First(column => column.BoardId == targetBoardId).Id;
        }
        var card = await CreateCardAndGetIdAsync(sourceColumnId, "Card");

        var response = await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={targetColumnId}&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.AreEqual(sourceColumnId, (await verificationDb.KanbanCards.FindAsync(card.Id))!.ColumnId);
    }

    // ── Recurrence ─────────────────────────────────────────

    [TestMethod]
    public async Task MoveCard_RecurringCard_ToCompleted_AdvancesDueDateAndRollsBackToNotStarted()
    {
        await LoginAsAdmin();
        var (boardId, notStartedColumnId) = await CreateBoardAndFirstColumnAsync();
        int completedColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            completedColumnId = db.KanbanColumns
                .First(c => c.BoardId == boardId && c.ColumnStatus == ColumnStatus.Completed).Id;
        }
        var card = await CreateCardAndGetIdAsync(notStartedColumnId, "Recurring task");

        // Set a 2-week recurrence with a known starting due date.
        var dueDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var setResponse = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "dueDate", dueDate.ToString("yyyy-MM-dd") },
                { "recurrenceInterval", "2" },
                { "recurrenceUnit", ((int)RecurrenceUnit.Week).ToString() }
            });
        Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);

        var response = await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={completedColumnId}&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var movePayload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.IsTrue(movePayload.GetProperty("RecurrenceApplied").GetBoolean());
        Assert.AreEqual("To Do", movePayload.GetProperty("RecurrenceTargetColumnName").GetString());

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var moved = await verificationDb.KanbanCards.FindAsync(card.Id);
        Assert.IsNotNull(moved);
        Assert.AreEqual(notStartedColumnId, moved!.ColumnId, "Card should roll back to the first NotStarted column.");
        Assert.AreEqual(2, moved.RecurrenceInterval);
        Assert.AreEqual(RecurrenceUnit.Week, moved.RecurrenceUnit);
        Assert.AreEqual(dueDate.AddDays(14), moved.DueDate);
        Assert.IsNull(moved.ActualEndTime, "ActualEndTime should be cleared on the new cycle.");
    }

    [TestMethod]
    public async Task MoveCard_NonRecurringCard_ToCompleted_DoesNotChangeDueDateOrColumn()
    {
        await LoginAsAdmin();
        var (boardId, notStartedColumnId) = await CreateBoardAndFirstColumnAsync();
        int completedColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            completedColumnId = db.KanbanColumns
                .First(c => c.BoardId == boardId && c.ColumnStatus == ColumnStatus.Completed).Id;
        }
        var card = await CreateCardAndGetIdAsync(notStartedColumnId, "One-off task");

        var dueDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var setResponse = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "dueDate", dueDate.ToString("yyyy-MM-dd") }
            });
        Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);

        var response = await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={completedColumnId}&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var moved = await verificationDb.KanbanCards.FindAsync(card.Id);
        Assert.AreEqual(completedColumnId, moved!.ColumnId);
        Assert.AreEqual(dueDate, moved.DueDate);
    }

    [TestMethod]
    public async Task UpdateCardDetails_RecurrenceIntervalWithoutUnit_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Invalid recurrence");

        var response = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "recurrenceInterval", "2" },
                { "recurrenceUnit", ((int)RecurrenceUnit.None).ToString() }
            });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateCardDetails_RecurrenceWithoutDueDate_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Recurring without due date");

        var response = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "recurrenceInterval", "2" },
                { "recurrenceUnit", ((int)RecurrenceUnit.Week).ToString() }
            });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateCardDetails_RecurrenceIntervalTooLarge_ReturnsBadRequest()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Recurrence too large");

        var response = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "dueDate", "2026-06-01" },
                { "recurrenceInterval", "366" },
                { "recurrenceUnit", ((int)RecurrenceUnit.Day).ToString() }
            });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MoveCard_RecurringCard_AlreadyInCompleted_DoesNotRecur()
    {
        await LoginAsAdmin();
        var (boardId, notStartedColumnId) = await CreateBoardAndFirstColumnAsync();
        int completedColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            completedColumnId = db.KanbanColumns
                .First(c => c.BoardId == boardId && c.ColumnStatus == ColumnStatus.Completed).Id;
        }
        var card = await CreateCardAndGetIdAsync(notStartedColumnId, "Recurring in done");

        var dueDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var setResponse = await PostAsync(
            "/Kanban/UpdateCardDetails",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "title", card.Title },
                { "dueDate", dueDate.ToString("yyyy-MM-dd") },
                { "recurrenceInterval", "1" },
                { "recurrenceUnit", ((int)RecurrenceUnit.Month).ToString() }
            });
        Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);

        // First move: NotStarted -> Completed. Should trigger recurrence and roll back.
        await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={completedColumnId}&newOrder=0",
            new Dictionary<string, string>());

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var afterFirst = await db.KanbanCards.FindAsync(card.Id);
            Assert.AreEqual(notStartedColumnId, afterFirst!.ColumnId);
            Assert.AreEqual(dueDate.AddMonths(1), afterFirst.DueDate);
        }

        // Second move: NotStarted -> Completed again. Should advance by another month.
        await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={completedColumnId}&newOrder=0",
            new Dictionary<string, string>());

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var afterSecond = await db.KanbanCards.FindAsync(card.Id);
            Assert.AreEqual(notStartedColumnId, afterSecond!.ColumnId);
            Assert.AreEqual(dueDate.AddMonths(2), afterSecond.DueDate);
        }

        // Intra-Completed reordering must NOT trigger recurrence.
        // Move the card into Completed directly via the db to skip the auto-rollback,
        // then re-order it within Completed.
        using (var setupScope = Server!.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var c = await db.KanbanCards.FindAsync(card.Id);
            c!.ColumnId = completedColumnId;
            await db.SaveChangesAsync();
        }

        await PostAsync(
            $"/Kanban/MoveCard?cardId={card.Id}&targetColumnId={completedColumnId}&newOrder=0",
            new Dictionary<string, string>());

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var afterThird = await db.KanbanCards.FindAsync(card.Id);
            Assert.AreEqual(dueDate.AddMonths(2), afterThird!.DueDate, "Reordering inside Completed must not re-advance the date.");
        }
    }

    // ── MoveColumn ─────────────────────────────────────────

    [TestMethod]
    public async Task MoveColumn_Reorder_ReturnsOk()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        var response = await PostAsync(
            $"/Kanban/MoveColumn?columnId={columnId}&newOrder=2",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MoveColumn_NonExistent_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await PostAsync(
            "/Kanban/MoveColumn?columnId=9999&newOrder=0",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DeleteColumn ───────────────────────────────────────

    [TestMethod]
    public async Task DeleteColumn_EmptyColumn_ReturnsOk()
    {
        await LoginAsAdmin();
        var boardId = await CreateBoardAndGetIdAsync("Board");

        // Add a fresh empty column so we know its ID and it has no cards.
        var newColResponse = await Http.PostAsync(
            $"/Kanban/CreateColumn?boardId={boardId}&name=Extra",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        newColResponse.EnsureSuccessStatusCode();
        var json = await newColResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var extraColId = doc.RootElement.GetProperty("Id").GetInt32();

        var response = await PostAsync(
            $"/Kanban/DeleteColumn?columnId={extraColId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var page = await Http.GetAsync($"/Kanban/Index?boardId={boardId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains("Extra"), "The empty 'Extra' column should be gone");
    }

    [TestMethod]
    public async Task DeleteColumn_WithCards_DeletesSuccessfully()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        await CreateCardAndGetIdAsync(columnId, "Blocking card");

        var response = await PostAsync(
            $"/Kanban/DeleteColumn?columnId={columnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var column = await db.KanbanColumns.FindAsync(columnId);
        Assert.IsNull(column);
    }

    [TestMethod]
    public async Task DeleteColumn_NonExistent_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await PostAsync(
            "/Kanban/DeleteColumn?columnId=9999",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────

    private async Task<HttpResponseMessage> CreateBoardAsync(string name)
    {
        return await PostForm("/Kanban/CreateBoard", new Dictionary<string, string>
        {
            { "name", name }
        });
    }

    private async Task<int> CreateBoardAndGetIdAsync(string name)
    {
        var response = await CreateBoardAsync(name);
        var location = response.Headers.Location!.OriginalString;
        return int.Parse(location.Split("boardId=")[1]);
    }

    private async Task<(int boardId, int firstColumnId)> CreateBoardAndFirstColumnAsync()
    {
        var boardId = await CreateBoardAndGetIdAsync("Board");

        var page = await Http.GetAsync($"/Kanban/Index?boardId={boardId}");
        var html = await page.Content.ReadAsStringAsync();

        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"data-column-id=""(\d+)""");
        var columnId = int.Parse(match.Groups[1].Value);

        return (boardId, columnId);
    }

    private async Task<(int Id, string Title)> CreateCardAndGetIdAsync(int columnId, string title)
    {
        var response = await PostAsync(
            $"/Kanban/CreateCard?columnId={columnId}&title={Uri.EscapeDataString(title)}",
            new Dictionary<string, string>());
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("Id").GetInt32(),
                doc.RootElement.GetProperty("Title").GetString()!);
    }

    /// <summary>POSTs form data with a CSRF token (for AJAX endpoints).</summary>
    private async Task<HttpResponseMessage> PostAsync(
        string url, Dictionary<string, string> data)
    {
        if (!data.ContainsKey("__RequestVerificationToken"))
        {
            var token = await GetAntiCsrfToken("/");
            data["__RequestVerificationToken"] = token;
        }
        return await Http.PostAsync(url, new FormUrlEncodedContent(data));
    }

    // ── Comments ───────────────────────────────────────────

    [TestMethod]
    public async Task GetComments_NonExistentCard_ReturnsNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Kanban/GetComments?cardId=9999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AddComment_ValidContent_AddsAndReturnsCommentDetails()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();
        var card = await CreateCardAndGetIdAsync(columnId, "Card with comments");

        var response = await PostAsync(
            $"/Kanban/AddComment",
            new Dictionary<string, string>
            {
                { "cardId", card.Id.ToString() },
                { "content", "This is a comment test" }
            });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("This is a comment test", doc.RootElement.GetProperty("Content").GetString());
        Assert.IsNotNull(doc.RootElement.GetProperty("AuthorName").GetString());
        Assert.IsNotNull(doc.RootElement.GetProperty("AuthorInitial").GetString());

        // Now test GetComments
        var getResponse = await Http.GetAsync($"/Kanban/GetComments?cardId={card.Id}");
        getResponse.EnsureSuccessStatusCode();
        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getJson);
        Assert.AreEqual(JsonValueKind.Array, getDoc.RootElement.ValueKind);
        Assert.AreEqual(1, getDoc.RootElement.GetArrayLength());
        Assert.AreEqual("This is a comment test", getDoc.RootElement[0].GetProperty("Content").GetString());
    }
}
