using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TestResult = Aiursoft.CommandFramework.Models.TestResult;
using Aiursoft.Kanban.Client;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class KanbanCliTests : TestBase
{
    private Task<TestResult> RunCli(string key, params string[] args) =>
        KanbanCommandApp.Create().TestRunAsync([.. args, "--server", Http.BaseAddress!.ToString(), "--api-key", key]);

    private static JObject ReadJson(TestResult result)
    {
        Assert.AreEqual(0, result.ProgramReturn, result.StdErr);
        Assert.AreEqual(string.Empty, result.StdErr);
        return JObject.Parse(result.StdOut);
    }

    [TestMethod]
    public async Task CliManagesColumnsAndCardsUsingPersonalKeyAndExistingApi()
    {
        var (email, _) = await RegisterAndLoginAsync();
        var key = await CreateKeyAsync();
        var boardId = await CreateBoardAsync(key);
        var list = ReadJson(await RunCli(key, "board", "list", "--json"));
        Assert.IsTrue(list["boards"]!.Any(b => b.Value<int>("id") == boardId));
        var first = ReadJson(await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "CLI review", "--json"));
        var columnId = first["board"]!["columns"]!.Last().Value<int>("id");
        var second = ReadJson(await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "CLI done", "--json"));
        var targetId = second["board"]!["columns"]!.Last().Value<int>("id");
        ReadJson(await RunCli(key, "column", "update", "--id", targetId.ToString(), "--name", "Accepted", "--status", "Completed", "--json"));
        var movedColumn = ReadJson(await RunCli(key, "column", "move", "--id", targetId.ToString(), "--order", "0", "--json"));
        Assert.AreEqual(targetId, movedColumn["board"]!["columns"]!.First().Value<int>("id"));
        var created = ReadJson(await RunCli(key, "card", "create", "--column-id", columnId.ToString(), "--title", "CLI acceptance", "--description", "Keep this description", "--json"));
        var cardId = created["card"]!.Value<int>("id");
        string ownerId;
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            ownerId = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var card = (await db.KanbanCards.FindAsync(cardId))!;
            card.AssignedUserId = ownerId;
            card.RecurrenceUnit = RecurrenceUnit.Week;
            card.RecurrenceInterval = 2;
            card.DueDate = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        var updated = ReadJson(await RunCli(key, "card", "update", "--id", cardId.ToString(), "--title", "Reviewed CLI acceptance", "--priority", "High", "--json"))["card"]!;
        Assert.AreEqual("Keep this description", updated.Value<string>("description"));
        Assert.AreEqual(ownerId, updated["assignedUser"]!.Value<string>("id"));
        Assert.AreEqual("Week", updated.Value<string>("recurrenceUnit"));
        Assert.AreEqual(2, updated.Value<int>("recurrenceInterval"));
        Assert.AreEqual(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), updated.Value<DateTime>("dueDate"));
        var dated = ReadJson(await RunCli(key, "card", "update", "--id", cardId.ToString(), "--planned-start", "2026-10-20T10:00:00+08:00", "--due-date", "2026-11-02T12:00:00Z", "--json"))["card"]!;
        Assert.AreEqual(new DateTime(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc), dated.Value<DateTime>("plannedStartTime"));
        Assert.AreEqual(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), dated.Value<DateTime>("dueDate"));
        var mine = ReadJson(await RunCli(key, "tasks", "--json"));
        Assert.IsTrue(mine["cards"]!.Any(c => c.Value<int>("id") == cardId));
        var search = ReadJson(await RunCli(key, "search", "--query", "Reviewed CLI acceptance", "--json"));
        Assert.IsTrue(search["cards"]!.Any(c => c.Value<int>("id") == cardId));
        ReadJson(await RunCli(key, "card", "comment", "--id", cardId.ToString(), "--content", "Ready for review", "--json"));
        ReadJson(await RunCli(key, "card", "move", "--id", cardId.ToString(), "--column-id", targetId.ToString(), "--order", "0", "--json"));
        var details = ReadJson(await RunCli(key, "card", "get", "--id", cardId.ToString(), "--json"))["card"]!;
        // Completing a recurring card uses the existing API rule: reset it to To Do.
        Assert.AreNotEqual(targetId, details.Value<int>("columnId"));
        Assert.AreEqual("Week", details.Value<string>("recurrenceUnit"));
        Assert.AreEqual("Ready for review", details["comments"]!.Last().Value<string>("content"));
        var ordinary = ReadJson(await RunCli(key, "card", "create", "--column-id", columnId.ToString(), "--title", "Non-recurring task", "--json"));
        var ordinaryId = ordinary["card"]!.Value<int>("id");
        var moved = ReadJson(await RunCli(key, "card", "move", "--id", ordinaryId.ToString(), "--column-id", targetId.ToString(), "--json"));
        Assert.AreEqual(targetId, moved["card"]!.Value<int>("columnId"));
        var cleared = ReadJson(await RunCli(key, "card", "update", "--id", cardId.ToString(), "--description", "", "--assigned-user-id", "", "--json"))["card"]!;
        Assert.IsTrue(string.IsNullOrEmpty(cleared.Value<string>("description")));
        Assert.AreEqual(JTokenType.Null, cleared["assignedUser"]!.Type);
        var text = await RunCli(key, "board", "get", "--id", boardId.ToString());
        Assert.AreEqual(0, text.ProgramReturn, text.StdErr);
        Assert.Contains("Reviewed CLI acceptance", text.StdOut);
        Assert.Contains("Accepted", text.StdOut);
        var deleted = ReadJson(await RunCli(key, "column", "delete", "--id", columnId.ToString(), "--json"));
        Assert.IsFalse(deleted["board"]!["columns"]!.Any(c => c.Value<int>("id") == columnId));
        Assert.DoesNotContain(key, text.StdOut + text.StdErr);
    }

    [TestMethod]
    public async Task CliRespectsPrivateReadOnlyEditableAndArchivedBoardAccess()
    {
        var (email, _) = await RegisterAndLoginAsync();
        var key = await CreateKeyAsync();
        int boardId;
        string ownerId;
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            ownerId = (await manager.FindByEmailAsync(email))!.Id;
            var otherEmail = $"cli-{Guid.NewGuid():N}@example.com";
            var other = new User { UserName = otherEmail, Email = otherEmail, DisplayName = "Other" };
            Assert.IsTrue((await manager.CreateAsync(other)).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var board = new KanbanBoard { Name = "Private CLI board", UserId = other.Id };
            db.KanbanBoards.Add(board);
            await db.SaveChangesAsync();
            boardId = board.Id;
        }
        var hidden = await RunCli(key, "board", "get", "--id", boardId.ToString(), "--json");
        Assert.AreNotEqual(0, hidden.ProgramReturn);
        Assert.Contains("NotFound", hidden.StdErr);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.BoardShares.Add(new BoardShare { BoardId = boardId, SharedWithUserId = ownerId, Permission = SharePermission.ReadOnly });
            await db.SaveChangesAsync();
        }
        ReadJson(await RunCli(key, "board", "get", "--id", boardId.ToString(), "--json"));
        var denied = await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "Denied", "--json");
        Assert.AreNotEqual(0, denied.ProgramReturn);
        Assert.Contains("Unauthorized", denied.StdErr);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.BoardShares.SingleAsync(s => s.BoardId == boardId)).Permission = SharePermission.Editable;
            await db.SaveChangesAsync();
        }
        ReadJson(await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "Allowed", "--json"));
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.KanbanBoards.FindAsync(boardId))!.IsArchived = true;
            await db.SaveChangesAsync();
        }
        var archived = await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "Denied", "--json");
        Assert.AreNotEqual(0, archived.ProgramReturn);
        Assert.Contains("Unauthorized", archived.StdErr);
        Assert.DoesNotContain(key, hidden.StdOut + hidden.StdErr + denied.StdErr + archived.StdErr);
    }

    [TestMethod]
    public async Task CliRejectsInvalidInputExpiredAndRevokedKeysWithoutPrintingSecrets()
    {
        await RegisterAndLoginAsync();
        var key = await CreateKeyAsync();
        var boardId = await CreateBoardAsync(key);
        var missingKey = await KanbanCommandApp.Create().TestRunAsync(["board", "list", "--server", Http.BaseAddress!.ToString()]);
        Assert.AreNotEqual(0, missingKey.ProgramReturn);
        Assert.Contains("--api-key", missingKey.StdErr);
        var missingServer = await KanbanCommandApp.Create().TestRunAsync(["board", "list", "--api-key", key]);
        Assert.AreNotEqual(0, missingServer.ProgramReturn);
        Assert.Contains("--server", missingServer.StdErr);
        var invalid = await RunCli(key, "column", "create", "--board-id", boardId.ToString(), "--name", "", "--json");
        Assert.AreNotEqual(0, invalid.ProgramReturn);
        Assert.Contains("InvalidInput", invalid.StdErr);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.PersonalApiKeys.SingleAsync(k => k.KeyHash == hash)).ExpirationTime = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var expired = await RunCli(key, "board", "list", "--json");
        Assert.AreNotEqual(0, expired.ProgramReturn);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.PersonalApiKeys.Remove(await db.PersonalApiKeys.SingleAsync(k => k.KeyHash == hash));
            await db.SaveChangesAsync();
        }
        var revoked = await RunCli(key, "board", "list", "--json");
        Assert.AreNotEqual(0, revoked.ProgramReturn);
        Assert.DoesNotContain(key, missingServer.StdOut + missingServer.StdErr + invalid.StdErr + expired.StdErr + revoked.StdErr);
    }

    private async Task<string> CreateKeyAsync()
    {
        using var created = await PostForm("/ApiKeys/Create", new Dictionary<string, string>
        {
            ["Name"] = "CLI acceptance", ["ExpirationDays"] = "1"
        }, "/ApiKeys/Create");
        AssertRedirect(created, "/ApiKeys");
        using var shown = await Http.GetAsync("/ApiKeys");
        shown.EnsureSuccessStatusCode();
        var match = Regex.Match(await shown.Content.ReadAsStringAsync(), @"kanban_[A-F0-9]{64}");
        Assert.IsTrue(match.Success);
        return match.Value;
    }

    private async Task<int> CreateBoardAsync(string key)
    {
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await Http.PostAsync("/api/v1/boards", new StringContent(
            JsonConvert.SerializeObject(new CreateBoardRequest { Name = "CLI work" }), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<BoardResponse>(await response.Content.ReadAsStringAsync())!.Board.Id;
    }
}
