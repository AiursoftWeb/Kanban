using System.Net;
using System.Security.Claims;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class AllBoardsTests : TestBase
{
    private readonly string _prefix = $"AllBoards-{Guid.NewGuid():N}";
    private readonly List<int> _boardIds = [];
    private readonly List<int> _cardIds = [];

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.KanbanCardComments.RemoveRange(await db.KanbanCardComments.Where(c => _cardIds.Contains(c.CardId)).ToListAsync());
            db.BoardShares.RemoveRange(await db.BoardShares.Where(s => _boardIds.Contains(s.BoardId)).ToListAsync());
            db.KanbanCards.RemoveRange(await db.KanbanCards.Where(c => _cardIds.Contains(c.Id)).ToListAsync());
            db.KanbanColumns.RemoveRange(await db.KanbanColumns.Where(c => _boardIds.Contains(c.BoardId)).ToListAsync());
            db.KanbanBoards.RemoveRange(await db.KanbanBoards.Where(b => _boardIds.Contains(b.Id) || b.Name.StartsWith(_prefix)).ToListAsync());
            await db.SaveChangesAsync();
        }
        await base.CleanServer();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Manager_CanBrowseRenameAndDelete_UnsharedBoard(bool archived)
    {
        var (boardId, cardId) = await CreateOtherBoard(archived);
        await RegisterManager();

        var all = await Http.GetStringAsync($"/Kanban/AllBoards?search={_prefix}");
        Assert.Contains($"{_prefix} Other private board", all);
        Assert.Contains(archived ? "Archived" : "Active", all);
        var board = await Http.GetStringAsync($"/Kanban/Index?boardId={boardId}");
        Assert.Contains("Other private board", board);
        Assert.Contains("Private card", board);
        var card = await Http.GetStringAsync($"/Cards/{cardId}");
        Assert.Contains("Private card", card);
        Assert.Contains("Private description", card);
        var comments = await Http.GetStringAsync($"/Kanban/GetComments?cardId={cardId}");
        Assert.Contains("Private comment", comments);

        var rename = await PostForm("/Kanban/RenameBoard", new()
        {
            ["boardId"] = boardId.ToString(), ["name"] = "  Test2  "
        }, "/Kanban/AllBoards");
        Assert.AreEqual(HttpStatusCode.OK, rename.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.AreEqual("Test2", (await db.KanbanBoards.FindAsync(boardId))!.Name);
        }

        var delete = await PostForm("/Kanban/DeleteBoard", new() { ["boardId"] = boardId.ToString() }, "/Kanban/AllBoards");
        Assert.AreEqual(HttpStatusCode.OK, delete.StatusCode);
        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await verificationDb.KanbanBoards.FindAsync(boardId));
        Assert.IsFalse(await verificationDb.KanbanColumns.AnyAsync(c => c.BoardId == boardId));
        Assert.IsNull(await verificationDb.KanbanCards.FindAsync(cardId));
        Assert.IsFalse(await verificationDb.BoardShares.AnyAsync(s => s.BoardId == boardId));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(AppPermissionNames.CanManageAnyBoardShare)]
    public async Task WithoutPermission_CannotListRenameOrDelete(string? permission)
    {
        var (boardId, _) = await CreateOtherBoard(false);
        await RegisterManager(permission);
        AssertDenied(await Http.GetAsync("/Kanban/AllBoards"));
        AssertDenied(await PostForm("/Kanban/RenameBoard", new()
        {
            ["boardId"] = boardId.ToString(), ["name"] = "Forbidden"
        }, "/Manage/ChangePassword"));
        AssertDenied(await PostForm("/Kanban/DeleteBoard", new() { ["boardId"] = boardId.ToString() }, "/Manage/ChangePassword"));
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.AreEqual($"{_prefix} Other private board", (await db.KanbanBoards.FindAsync(boardId))!.Name);
    }

    [TestMethod]
    public async Task Manager_PersonalListsStayScoped_AndCardsStayReadOnly()
    {
        var (boardId, cardId) = await CreateOtherBoard(false);
        await RegisterManager();
        var mine = await Http.GetStringAsync("/Kanban/Index");
        var shared = await Http.GetStringAsync("/Kanban/SharedWithMe");
        Assert.DoesNotContain("Other private board", mine);
        Assert.DoesNotContain("Other private board", shared);
        Assert.Contains("All Boards", mine);
        AssertDenied(await Http.PostAsync($"/Kanban/CreateColumn?boardId={boardId}&name=Forbidden", new FormUrlEncodedContent(new Dictionary<string, string>())));
        AssertDenied(await PostForm("/Kanban/DeleteCard", new() { ["cardId"] = cardId.ToString() }, "/Kanban/AllBoards"));
    }

    [TestMethod]
    public async Task Manager_RenameAndDeleteRequireCsrf_AndNamesAreValidated()
    {
        var (boardId, _) = await CreateOtherBoard(false);
        await RegisterManager();
        foreach (var action in new[] { "RenameBoard", "DeleteBoard" })
        {
            var response = await PostForm($"/Kanban/{action}", new()
            {
                ["boardId"] = boardId.ToString(), ["name"] = "No token"
            }, includeToken: false);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var name in new[] { "   ", new string('x', 101) })
        {
            var response = await PostForm("/Kanban/RenameBoard", new()
            {
                ["boardId"] = boardId.ToString(), ["name"] = name
            }, "/Kanban/AllBoards");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm("/Kanban/DeleteBoard", new() { ["boardId"] = int.MaxValue.ToString() }, "/Kanban/AllBoards")).StatusCode);
    }

    [TestMethod]
    public async Task Manager_CanFindOrphanedBoards_AndPageThroughAllBoards()
    {
        await RegisterManager();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.KanbanBoards.AddRange(Enumerable.Range(0, 51).Select(i => new KanbanBoard { Name = $"{_prefix} Orphan-{i:D2}" }));
            await db.SaveChangesAsync();
        }
        var first = await Http.GetStringAsync($"/Kanban/AllBoards?search={_prefix}");
        Assert.Contains("Orphan-00", first);
        Assert.DoesNotContain("Orphan-50", first);
        var second = await Http.GetStringAsync($"/Kanban/AllBoards?search={_prefix}&page=2");
        Assert.Contains("Orphan-50", second);
        Assert.Contains("Deleted user", second);
        var found = await Http.GetStringAsync($"/Kanban/AllBoards?search={_prefix}%20Orphan-50&page=999");
        Assert.Contains("Orphan-50", found);
        Assert.DoesNotContain("Orphan-00", found);
        var empty = await Http.GetStringAsync("/Kanban/AllBoards?search=Nonexistent");
        Assert.Contains("No boards found.", empty);
    }

    private async Task<(int boardId, int cardId)> CreateOtherBoard(bool archived)
    {
        var (email, _) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var board = new KanbanBoard { Name = $"{_prefix} Other private board", UserId = user.Id, IsArchived = archived };
        var column = new KanbanColumn { Name = "Private column", Board = board };
        var card = new KanbanCard { Title = "Private card", Description = "Private description", Column = column };
        db.KanbanCards.Add(card);
        db.KanbanCardComments.Add(new KanbanCardComment { Card = card, AuthorId = user.Id, Content = "Private comment" });
        db.BoardShares.Add(new BoardShare { BoardId = 0, Board = board, SharedWithUserId = user.Id, Permission = SharePermission.ReadOnly });
        await db.SaveChangesAsync();
        _boardIds.Add(board.Id);
        _cardIds.Add(card.Id);
        await LogoutAsync();
        return (board.Id, card.Id);
    }

    private async Task RegisterManager(string? permission = AppPermissionNames.CanManageAnyBoard)
    {
        var (email, password) = await RegisterAndLoginAsync();
        if (permission == null) return;
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await manager.FindByEmailAsync(email))!;
            Assert.IsTrue((await manager.AddClaimAsync(user, new Claim(AppPermissions.Type, permission))).Succeeded);
        }
        await LogoutAsync();
        await LoginAsync(email, password);
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
            (response.StatusCode == HttpStatusCode.Found && response.Headers.Location!.OriginalString.Contains("403")));
    }
}
