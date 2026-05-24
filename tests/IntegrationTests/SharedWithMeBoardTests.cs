using System.Net;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class SharedWithMeBoardTests : TestBase
{
    [TestMethod]
    public async Task SharedWithMe_Page_LoadsAndContainsCorrectLinks()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var boardId = await CreateBoardWithOwner(ownerId, "Shared Test Board");
        await LogoutAsync();

        var (viewerEmail, _) = await RegisterAndLoginAsync();
        var viewerId = await GetUserIdByEmailAsync(viewerEmail);
        await CreateShare(boardId, viewerId, SharePermission.ReadOnly);

        var response = await Http.GetAsync("/Kanban/SharedWithMe");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"/PublicKanban/View/{boardId}", html);
    }

    private async Task<string> GetUserIdByEmailAsync(string email)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        return user.Id;
    }

    private async Task<int> CreateBoardWithOwner(string userId, string name)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var board = new KanbanBoard
        {
            Name = name,
            UserId = userId
        };

        db.KanbanBoards.Add(board);
        db.KanbanColumns.AddRange(
            new KanbanColumn { Name = "To Do", Order = 0, Board = board },
            new KanbanColumn { Name = "In Progress", Order = 1, Board = board },
            new KanbanColumn { Name = "Done", Order = 2, Board = board });

        await db.SaveChangesAsync();
        return board.Id;
    }

    private async Task CreateShare(int boardId, string userId, SharePermission permission)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            SharedWithUserId = userId,
            Permission = permission
        });

        await db.SaveChangesAsync();
    }
}
