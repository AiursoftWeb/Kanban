using System.Net;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.Services.Auditing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class AuditLogsTests : TestBase
{
    [TestMethod]
    public async Task SuccessfulKanbanWriteIsAddedToAuditBuffer()
    {
        await LoginAsAdmin();
        var buffer = GetService<AuditLogBuffer>();
        buffer.Drain([]);

        var response = await PostForm("/Kanban/CreateBoard", new Dictionary<string, string>
        {
            ["name"] = "Audited board"
        });

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var logs = new List<AuditLog>();
        buffer.Drain(logs);
        Assert.IsTrue(logs.Any(log =>
            log.Action == "Kanban.CreateBoard" &&
            log.Details.Contains("Audited board", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SemanticCommentAuditDoesNotStoreCommentContent()
    {
        await LoginAsAdmin();
        var buffer = GetService<AuditLogBuffer>();
        buffer.Drain([]);
        var columnId = await CreateBoardAndGetFirstColumnIdAsync("Audit content board");
        var cardId = await CreateCardAndGetIdAsync(columnId, "Comment audit card");
        var actorUserId = await GetAdminUserIdAsync();
        buffer.Drain([]);

        await GetService<IMediator>().Publish(new CardCommentAddedEvent(
            CardId: cardId,
            CommentId: 123,
            ActorUserId: actorUserId));

        var logs = new List<AuditLog>();
        buffer.Drain(logs);
        var commentLog = logs.Single(log => log.Action == "Kanban.AddComment");

        Assert.Contains("\"CommentId\"", commentLog.Details);
        Assert.DoesNotContain("\"Content\"", commentLog.Details);
    }

    [TestMethod]
    public async Task UserCanOpenOwnLogsButNotAllUsersLogs()
    {
        await RegisterAndLoginAsync();

        var mineResponse = await Http.GetAsync("/AuditLogs/Mine");
        var allResponse = await Http.GetAsync("/AuditLogs/All");

        Assert.AreEqual(HttpStatusCode.OK, mineResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, allResponse.StatusCode);
        Assert.Contains("/Error/Code403", allResponse.Headers.Location?.OriginalString ?? string.Empty);
    }

    [TestMethod]
    public async Task AdministratorCanOpenAllUsersLogs()
    {
        await LoginAsAdmin();

        var response = await Http.GetAsync("/AuditLogs/All");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<int> CreateBoardAndGetFirstColumnIdAsync(string name)
    {
        var response = await PostForm("/Kanban/CreateBoard", new Dictionary<string, string>
        {
            ["name"] = name
        });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;
        var page = await Http.GetAsync(location);
        var html = await page.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(html, @"data-column-id=""(\d+)""");
        Assert.IsTrue(match.Success);
        return int.Parse(match.Groups[1].Value);
    }

    private async Task<int> CreateCardAndGetIdAsync(int columnId, string title)
    {
        var response = await PostForm($"/Kanban/CreateCard?columnId={columnId}&title={Uri.EscapeDataString(title)}",
            new Dictionary<string, string>(),
            tokenUrl: "/");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Id").GetInt32();
    }

    private async Task<string> GetAdminUserIdAsync()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        return await db.Users
            .Where(user => user.Email == "admin@default.com")
            .Select(user => user.Id)
            .SingleAsync();
    }
}
