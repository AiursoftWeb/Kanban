using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Aiursoft.Kanban.Entities;

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
}
