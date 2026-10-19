using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.ApiKeysViewModels;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class PersonalApiKeyTests : TestBase
{
    [TestMethod]
    public async Task CreateShowsSecretOnceAndPersistsOnlyHashWithFifteenYearLimit()
    {
        await RegisterAndLoginAsync();
        var token = await CreateKeyAsync(CreateViewModel.MaximumExpirationDays);
        await using var scope = Server!.Services.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().PersonalApiKeys.SingleAsync(k => k.KeyHash == Hash(token));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), key.KeyHash);
        Assert.AreNotEqual(token, key.KeyHash);
        Assert.AreEqual(token[..15], key.Prefix);
        Assert.IsLessThanOrEqualTo(key.CreatedAt.AddYears(15), key.ExpirationTime);
        Assert.IsGreaterThan(key.CreatedAt.AddYears(14), key.ExpirationTime);
        var again = await Http.GetAsync("/ApiKeys");
        Assert.IsFalse((await again.Content.ReadAsStringAsync()).Contains(token, StringComparison.Ordinal));
        Assert.IsTrue(again.Headers.CacheControl?.NoStore);
    }

    [TestMethod]
    public async Task InvalidValidityAndMissingCsrfCannotCreateKey()
    {
        var (email, _) = await RegisterAndLoginAsync();
        foreach (var days in new[] { -1, 0, CreateViewModel.MaximumExpirationDays + 1, int.MaxValue })
        {
            using var response = await PostForm("/ApiKeys/Create", new Dictionary<string, string>
            {
                ["Name"] = "Rejected", ["ExpirationDays"] = days.ToString()
            }, "/ApiKeys/Create");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        using var noCsrf = await Http.PostAsync("/ApiKeys/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Rejected", ["ExpirationDays"] = "1"
        }));
        Assert.AreEqual(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await using var scope = Server!.Services.CreateAsyncScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email))!;
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().PersonalApiKeys.CountAsync(k => k.UserId == owner.Id));
    }

    [TestMethod]
    public async Task KeyCreatesAndReadsBoardsAndRevocationTakesEffectImmediately()
    {
        await RegisterAndLoginAsync();
        var token = await CreateKeyAsync();
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await Http.PostAsync("/api/v1/boards", Json(new CreateBoardRequest { Name = "Key board" }));
        created.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(await created.Content.ReadAsStringAsync())!;
        using var read = await Http.GetAsync($"/api/v1/boards/{board.Board.Id}");
        read.EnsureSuccessStatusCode();
        int keyId;
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            keyId = await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().PersonalApiKeys.Where(k => k.KeyHash == Hash(token)).Select(k => k.Id).SingleAsync();
        }
        using var revoked = await PostForm($"/ApiKeys/Revoke/{keyId}", new Dictionary<string, string>(), "/ApiKeys");
        Assert.AreEqual(HttpStatusCode.Found, revoked.StatusCode);
        using var denied = await Http.GetAsync("/api/v1/boards");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [TestMethod]
    public async Task AnotherUserCannotListOrRevokePersonalKey()
    {
        await RegisterAndLoginAsync();
        var token = await CreateKeyAsync();
        int id;
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().PersonalApiKeys.Where(k => k.KeyHash == Hash(token)).Select(k => k.Id).SingleAsync();
        }
        await LogoutAsync();
        await RegisterAndLoginAsync();
        using var list = await Http.GetAsync("/ApiKeys");
        Assert.IsFalse((await list.Content.ReadAsStringAsync()).Contains(token[..15], StringComparison.Ordinal));
        using var revoke = await PostForm($"/ApiKeys/Revoke/{id}", new Dictionary<string, string>(), "/ApiKeys/Create");
        Assert.AreEqual(HttpStatusCode.NotFound, revoke.StatusCode);
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var stillValid = await Http.GetAsync("/api/v1/boards");
        stillValid.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task ExpiredAndMalformedKeysAreRejected()
    {
        await RegisterAndLoginAsync();
        var token = await CreateKeyAsync();
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.PersonalApiKeys.SingleAsync(k => k.KeyHash == Hash(token))).ExpirationTime = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        foreach (var invalid in new[] { token, "kanban_bad", "kanban_" + new string('0', 64) })
        {
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalid);
            using var read = await Http.GetAsync("/api/v1/boards");
            Assert.AreEqual(HttpStatusCode.Unauthorized, read.StatusCode);
        }
    }

    [TestMethod]
    public async Task KeyUsesCurrentUserPermissionsRatherThanIssuanceSnapshot()
    {
        var (email, _) = await RegisterAndLoginAsync();
        var token = await CreateKeyAsync();
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var path = $"/api/v1/cards/{int.MaxValue}/actual-times";
        using var denied = await Http.PutAsync(path, Json(new UpdateCardActualTimesRequest()));
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        var claim = new Claim(AppPermissions.Type, AppPermissionNames.EditActualTime);
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await manager.FindByEmailAsync(email))!;
            Assert.IsTrue((await manager.AddClaimAsync(user, claim)).Succeeded);
        }
        using var allowed = await Http.PutAsync(path, Json(new UpdateCardActualTimesRequest()));
        Assert.AreEqual(HttpStatusCode.NotFound, allowed.StatusCode);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.IsTrue((await manager.RemoveClaimAsync((await manager.FindByEmailAsync(email))!, claim)).Succeeded);
        }
        using var removed = await Http.PutAsync(path, Json(new UpdateCardActualTimesRequest()));
        Assert.AreEqual(HttpStatusCode.Forbidden, removed.StatusCode);
    }

    [TestMethod]
    public async Task SharingChangesAndArchivedBoardsAreEnforcedForTheKeyOwner()
    {
        var (email, _) = await RegisterAndLoginAsync();
        var token = await CreateKeyAsync();
        int boardId;
        int columnId;
        string ownerId;
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            ownerId = (await manager.FindByEmailAsync(email))!.Id;
            var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
            var other = new User { UserName = otherEmail, Email = otherEmail, DisplayName = "Other" };
            Assert.IsTrue((await manager.CreateAsync(other)).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var board = new KanbanBoard { Name = "Other private board", UserId = other.Id };
            var column = new KanbanColumn { Name = "Work", Board = board };
            db.KanbanColumns.Add(column);
            await db.SaveChangesAsync();
            boardId = board.Id;
            columnId = column.Id;
        }
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var hidden = await Http.GetAsync($"/api/v1/boards/{boardId}");
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.BoardShares.Add(new BoardShare { BoardId = boardId, SharedWithUserId = ownerId, Permission = SharePermission.ReadOnly });
            await db.SaveChangesAsync();
        }
        using var readable = await Http.GetAsync($"/api/v1/boards/{boardId}");
        readable.EnsureSuccessStatusCode();
        using var noWrite = await Http.PostAsync($"/api/v1/columns/{columnId}/cards", Json(new CreateCardRequest { Title = "Denied" }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, noWrite.StatusCode);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.BoardShares.SingleAsync(share => share.BoardId == boardId)).Permission = SharePermission.Editable;
            await db.SaveChangesAsync();
        }
        using var writable = await Http.PostAsync($"/api/v1/columns/{columnId}/cards", Json(new CreateCardRequest { Title = "Allowed" }));
        writable.EnsureSuccessStatusCode();
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            (await db.KanbanBoards.FindAsync(boardId))!.IsArchived = true;
            await db.SaveChangesAsync();
        }
        using var archived = await Http.PostAsync($"/api/v1/columns/{columnId}/cards", Json(new CreateCardRequest { Title = "Denied" }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, archived.StatusCode);
    }

    private async Task<string> CreateKeyAsync(int days = 365)
    {
        using var response = await PostForm("/ApiKeys/Create", new Dictionary<string, string>
        {
            ["Name"] = "Automation", ["ExpirationDays"] = days.ToString()
        }, "/ApiKeys/Create");
        AssertRedirect(response, "/ApiKeys");
        using var shown = await Http.GetAsync("/ApiKeys");
        shown.EnsureSuccessStatusCode();
        var match = Regex.Match(await shown.Content.ReadAsStringAsync(), @"kanban_[A-F0-9]{64}");
        Assert.IsTrue(match.Success);
        return match.Value;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static StringContent Json(object value) => new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
}
