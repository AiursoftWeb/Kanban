using System.Net;
using System.Text.Json;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Auditing;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class AuditLogsTests : TestBase
{
    [TestMethod]
    public async Task AuditDisabledDoesNotAddKanbanWriteToAuditBuffer()
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
        Assert.AreEqual(0, logs.Count);
    }

    [TestMethod]
    public void AuditDetailFilterRemovesSensitiveAgentArguments()
    {
        var details = AuditDetailFilter.ToSafeDictionary(new Dictionary<string, object?>
        {
            ["columnId"] = 1,
            ["cardsJson"] = """[{"title":"Card","description":"private details"}]""",
            ["description"] = "private details",
            ["token"] = "secret-token"
        });

        Assert.IsTrue(details.ContainsKey("columnId"));
        Assert.IsFalse(details.ContainsKey("cardsJson"));
        Assert.IsFalse(details.ContainsKey("description"));
        Assert.IsFalse(details.ContainsKey("token"));
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

    [TestMethod]
    public async Task ActualTimeChangesIncludeActorAndBeforeAndAfterValuesInAuditLog()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == "admin@default.com");
        var card = new KanbanCard { Title = "Historical dates" };
        var board = new KanbanBoard
        {
            Name = "Audit dates", UserId = user.Id,
            Columns = [new KanbanColumn { Name = "Done", Cards = [card] }]
        };
        db.KanbanBoards.Add(board);
        await db.SaveChangesAsync();
        var buffer = new AuditLogBuffer(NullLogger<AuditLogBuffer>.Instance);
        var service = new AuditLogService(buffer, new AuditLogContext(), new HttpContextAccessor(), new EnabledAuditOptions());
        var handler = new AuditEventHandlers(db, service);
        var oldStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newStart = oldStart.AddYears(-1);
        await handler.Handle(new CardUpdatedEvent(card.Id, user.Id, ["actual start time"],
            new CardActualTimeChange(oldStart, null, newStart, null)), CancellationToken.None);
        var logs = new List<AuditLog>();
        buffer.Drain(logs);
        var log = logs.Single();
        Assert.AreEqual(user.Id, log.UserId);
        using var details = JsonDocument.Parse(log.Details);
        var change = details.RootElement.GetProperty("ActualTimeChange");
        Assert.AreEqual(oldStart, change.GetProperty("OldStartTime").GetDateTime());
        Assert.AreEqual(newStart, change.GetProperty("NewStartTime").GetDateTime());
        Assert.AreEqual(JsonValueKind.Null, change.GetProperty("NewEndTime").ValueKind);
    }

    private sealed class EnabledAuditOptions : IOptionsMonitor<ClickhouseOptions>
    {
        public ClickhouseOptions CurrentValue { get; } = new() { Enabled = true };
        public ClickhouseOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ClickhouseOptions, string?> listener) => null;
    }

}
