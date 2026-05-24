using System.Net;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class BoardSharingTests : TestBase
{
    [TestMethod]
    public async Task Owner_CanEdit_TheirOwnBoard()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var boardId = await CreateBoardWithOwner(ownerId, "Owner Board");

        var response = await Http.GetAsync($"/Kanban/Index?boardId={boardId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Owner Board", html);
    }

    [TestMethod]
    public async Task NonOwner_WithoutShare_CannotView_Board()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Private Board");
        await LogoutAsync();

        await RegisterAndLoginAsync();
        var response = await Http.GetAsync($"/PublicKanban/View/{boardId}");

        Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Found);
    }

    [TestMethod]
    public async Task User_WithReadOnlyShare_CanView_ButCannotEditColumn()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Shared Board");
        await LogoutAsync();

        var viewerId = await RegisterUserAndGetIdAsync();
        await CreateShare(boardId, viewerId, null, SharePermission.ReadOnly);

        var viewResponse = await Http.GetAsync($"/PublicKanban/View/{boardId}");
        Assert.AreEqual(HttpStatusCode.OK, viewResponse.StatusCode);

        var editResponse = await Http.PostAsync(
            $"/Kanban/CreateColumn?boardId={boardId}&name=Blocked",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.IsTrue(editResponse.StatusCode == HttpStatusCode.Forbidden || editResponse.StatusCode == HttpStatusCode.Found);
    }

    [TestMethod]
    public async Task User_WithEditableShare_CanEdit()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Editable Board");
        await LogoutAsync();

        var editorId = await RegisterUserAndGetIdAsync();
        await CreateShare(boardId, editorId, null, SharePermission.Editable);

        var response = await Http.PostAsync(
            $"/Kanban/CreateColumn?boardId={boardId}&name=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousUser_CanView_PublicBoard()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Public Board", isPublic: true);
        await LogoutAsync();

        var response = await Http.GetAsync($"/PublicKanban/View/{boardId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousUser_CannotView_PrivateBoard()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Private Board");
        await LogoutAsync();

        var response = await Http.GetAsync($"/PublicKanban/View/{boardId}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        StringAssert.Contains(response.Headers.Location?.OriginalString ?? string.Empty, "Login");
    }

    [TestMethod]
    public async Task SharedUser_CannotManageShares()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Owner Board");
        await LogoutAsync();

        var editorId = await RegisterUserAndGetIdAsync();
        await CreateShare(boardId, editorId, null, SharePermission.Editable);

        var response = await Http.GetAsync($"/Kanban/ManageShares/{boardId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> RegisterUserAndGetIdAsync()
    {
        var (email, _) = await RegisterAndLoginAsync();
        return await GetUserIdByEmailAsync(email);
    }

    private async Task<string> GetUserIdByEmailAsync(string email)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        return user.Id;
    }

    private async Task<int> CreateBoardWithOwner(string userId, string name, bool isPublic = false)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var board = new KanbanBoard
        {
            Name = name,
            UserId = userId,
            IsPublic = isPublic
        };

        db.KanbanBoards.Add(board);
        db.KanbanColumns.AddRange(
            new KanbanColumn { Name = "To Do", Order = 0, Board = board },
            new KanbanColumn { Name = "In Progress", Order = 1, Board = board },
            new KanbanColumn { Name = "Done", Order = 2, Board = board });

        await db.SaveChangesAsync();
        return board.Id;
    }

    private async Task CreateShare(int boardId, string? userId, string? roleId, SharePermission permission)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            SharedWithUserId = userId,
            SharedWithRoleId = roleId,
            Permission = permission
        });

        await db.SaveChangesAsync();
    }
}
