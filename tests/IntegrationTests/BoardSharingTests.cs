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

    [TestMethod]
    public async Task TransferCard_ToEditableSharedBoard_CopiesCardAndClearsAssignee()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var targetOwnerId = await RegisterUserAndGetIdAsync();
        var targetBoardId = await CreateBoardWithOwner(targetOwnerId, "Target Board");
        await CreateShare(targetBoardId, sourceOwnerId, null, SharePermission.Editable);
        await LogoutAsync();
        await LoginAsync(sourceOwnerEmail, sourceOwnerPassword);

        int cardId;
        int targetColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var sourceColumnId = await db.KanbanColumns
                .Where(column => column.BoardId == sourceBoardId)
                .OrderBy(column => column.Order)
                .Select(column => column.Id)
                .FirstAsync();
            targetColumnId = await db.KanbanColumns
                .Where(column => column.BoardId == targetBoardId)
                .OrderBy(column => column.Order)
                .Select(column => column.Id)
                .FirstAsync();
            var label = new KanbanLabel { Name = "Transfer", Color = "#3B82F6" };
            db.KanbanCards.Add(new KanbanCard
            {
                Title = "Existing target card",
                ColumnId = targetColumnId,
                Order = 0
            });
            var card = new KanbanCard
            {
                Title = "Move me",
                Description = "Keep details",
                ColumnId = sourceColumnId,
                Order = 0,
                Priority = Priority.High,
                AssignedUserId = sourceOwnerId,
                PlannedStartTime = DateTime.UtcNow.Date,
                DueDate = DateTime.UtcNow.Date.AddDays(3)
            };
            db.KanbanCards.Add(card);
            db.KanbanLabels.Add(label);
            db.KanbanCardLabels.Add(new KanbanCardLabel { Card = card, Label = label });
            db.KanbanCardComments.Add(new KanbanCardComment
            {
                Card = card,
                AuthorId = sourceOwnerId,
                Content = "Do not copy history"
            });
            await db.SaveChangesAsync();
            cardId = card.Id;
        }

        var targetsResponse = await Http.GetAsync($"/Kanban/GetTransferTargets?cardId={cardId}");
        targetsResponse.EnsureSuccessStatusCode();
        Assert.Contains("Target Board", await targetsResponse.Content.ReadAsStringAsync());

        var transferResponse = await Http.PostAsync(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.IsNull(await db.KanbanCards.FindAsync(cardId));

            var transferredCard = await db.KanbanCards
                .Include(card => card.CardLabels)
                .SingleAsync(card => card.ColumnId == targetColumnId && card.Title == "Move me");
            Assert.AreNotEqual(cardId, transferredCard.Id);
            Assert.AreEqual("Move me", transferredCard.Title);
            Assert.AreEqual("Keep details", transferredCard.Description);
            Assert.AreEqual(1, transferredCard.Order);
            Assert.AreEqual(Priority.High, transferredCard.Priority);
            Assert.IsNull(transferredCard.AssignedUserId);
            Assert.HasCount(1, transferredCard.CardLabels);
            Assert.AreEqual(1, await db.KanbanCardLabels.CountAsync());
            Assert.IsFalse(await db.KanbanCardComments.AnyAsync());
        }
    }

    [TestMethod]
    public async Task TransferCard_ToReadOnlySharedBoard_ReturnsForbidden()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var targetOwnerId = await RegisterUserAndGetIdAsync();
        var targetBoardId = await CreateBoardWithOwner(targetOwnerId, "Read-only Board");
        await CreateShare(targetBoardId, sourceOwnerId, null, SharePermission.ReadOnly);
        await LogoutAsync();
        await LoginAsync(sourceOwnerEmail, sourceOwnerPassword);

        int cardId;
        int targetColumnId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var sourceColumnId = await db.KanbanColumns
                .Where(column => column.BoardId == sourceBoardId)
                .Select(column => column.Id)
                .FirstAsync();
            targetColumnId = await db.KanbanColumns
                .Where(column => column.BoardId == targetBoardId)
                .Select(column => column.Id)
                .FirstAsync();
            var card = new KanbanCard { Title = "Stay here", ColumnId = sourceColumnId };
            db.KanbanCards.Add(card);
            await db.SaveChangesAsync();
            cardId = card.Id;
        }

        var targetsResponse = await Http.GetAsync($"/Kanban/GetTransferTargets?cardId={cardId}");
        targetsResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain("Read-only Board", await targetsResponse.Content.ReadAsStringAsync());

        var transferResponse = await Http.PostAsync(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.IsTrue(transferResponse.StatusCode == HttpStatusCode.Forbidden ||
                      transferResponse.StatusCode == HttpStatusCode.Found);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNotNull(await verificationDb.KanbanCards.FindAsync(cardId));
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
