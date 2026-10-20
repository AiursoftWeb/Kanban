using System.Net;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Aiursoft.Kanban.Models.HomeViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.UiStack.Views.Shared.Components.SideMenu;

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

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Sidebar_SeparatesOwnedAndSharedBoards(bool hasOwned, bool hasShared)
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var sharedId = await CreateBoardWithOwner(ownerId, "A shared board");
        var archivedId = await CreateBoardWithOwner(ownerId, "Archived board");
        await LogoutAsync();
        var (viewerEmail, _) = await RegisterAndLoginAsync();
        var viewerId = await GetUserIdByEmailAsync(viewerEmail);
        var ownedId = hasOwned ? await CreateBoardWithOwner(viewerId, "Z owned board") : 0;

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        (await db.KanbanBoards.SingleAsync(b => b.Id == archivedId)).IsArchived = true;
        var role = new IdentityRole($"Board viewers {Guid.NewGuid()}");
        role.NormalizedName = role.Name!.ToUpperInvariant();
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = viewerId });
        if (hasShared)
        {
            await CreateShare(sharedId, viewerId, SharePermission.ReadOnly);
            db.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(), BoardId = sharedId,
                SharedWithRoleId = role.Id, Permission = SharePermission.ReadOnly
            });
        }
        await CreateShare(archivedId, viewerId, SharePermission.ReadOnly);
        if (hasOwned) await CreateShare(ownedId, viewerId, SharePermission.ReadOnly);
        await db.SaveChangesAsync();

        var user = await db.Users.SingleAsync(u => u.Id == viewerId);
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = await scope.ServiceProvider.GetRequiredService<SignInManager<User>>()
                .CreateUserPrincipalAsync(user)
        };
        context.Request.RouteValues["controller"] = "Kanban";
        context.Request.QueryString = new QueryString($"?boardId={sharedId}");
        var model = new IndexViewModel();
        scope.ServiceProvider.GetRequiredService<ViewModelArgsInjector>().Inject(context, model);
        var groups = model.Sidebar!.SideMenu!.Groups
            .Where(g => g.Name == "My Boards" || g.Name == "Shared with Me").ToList();
        CollectionAssert.AreEqual(
            (hasOwned ? new[] { "My Boards" } : Array.Empty<string>())
            .Concat(hasShared ? new[] { "Shared with Me" } : Array.Empty<string>()).ToArray(),
            groups.Select(g => g.Name.ToString()).ToArray());
        Assert.IsFalse(model.Sidebar.SideMenu.Groups.Any(g => g.Name == "All Boards"));
        if (hasOwned)
        {
            var item = (LinkSideBarItem)groups.Single(g => g.Name == "My Boards").Items.Single();
            Assert.AreEqual($"/Kanban/Index?boardId={ownedId}", item.Href);
            Assert.IsFalse(item.IsActive);
        }
        if (hasShared)
        {
            var sharedItems = groups.Single(g => g.Name == "Shared with Me").Items;
            Assert.HasCount(1, sharedItems, string.Join(", ", sharedItems.Select(i => i.Text)));
            var item = (LinkSideBarItem)sharedItems.Single();
            Assert.AreEqual($"/Kanban/Index?boardId={sharedId}", item.Href);
            Assert.IsTrue(item.IsActive);
        }
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
