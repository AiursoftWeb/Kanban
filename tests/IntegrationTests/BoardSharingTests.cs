using System.Net;
using System.Net.Http.Json;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class BoardSharingTests : TestBase
{
    private sealed record TransferCardResult(int Id);

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
    public async Task User_WithReadOnlyShare_CannotDeleteCard()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Read-only Board");
        var cardId = await CreateCard(boardId, "Keep me");
        await LogoutAsync();

        var viewerId = await RegisterUserAndGetIdAsync();
        await CreateShare(boardId, viewerId, null, SharePermission.ReadOnly);

        var response = await PostForm(
            $"/Kanban/DeleteCard?cardId={cardId}",
            new Dictionary<string, string>());

        Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                      response.StatusCode == HttpStatusCode.Found);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNotNull(await verificationDb.KanbanCards.FindAsync(cardId));
    }

    [TestMethod]
    public async Task User_WithEditableShare_CanDeleteCard()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Editable Board");
        var cardId = await CreateCard(boardId, "Delete me");
        await LogoutAsync();

        var editorId = await RegisterUserAndGetIdAsync();
        await CreateShare(boardId, editorId, null, SharePermission.Editable);

        var response = await PostForm(
            $"/Kanban/DeleteCard?cardId={cardId}",
            new Dictionary<string, string>());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await verificationDb.KanbanCards.FindAsync(cardId));
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
                DueDate = DateTime.UtcNow.Date.AddDays(3),
                ActualStartTime = DateTime.UtcNow.AddDays(-2),
                ActualEndTime = DateTime.UtcNow.AddDays(-1),
                RecurrenceInterval = 2,
                RecurrenceUnit = RecurrenceUnit.Week
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

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);
        var transferResult = await transferResponse.Content.ReadFromJsonAsync<TransferCardResult>();
        Assert.IsNotNull(transferResult);
        Assert.AreNotEqual(cardId, transferResult.Id);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.IsNull(await db.KanbanCards.FindAsync(cardId));

            var transferredCard = await db.KanbanCards
                .Include(card => card.CardLabels)
                .SingleAsync(card => card.ColumnId == targetColumnId && card.Title == "Move me");
            Assert.AreEqual(transferResult.Id, transferredCard.Id);
            Assert.AreEqual("Move me", transferredCard.Title);
            Assert.AreEqual("Keep details", transferredCard.Description);
            Assert.AreEqual(1, transferredCard.Order);
            Assert.AreEqual(Priority.High, transferredCard.Priority);
            Assert.IsNull(transferredCard.AssignedUserId);
            Assert.IsNull(transferredCard.ActualStartTime);
            Assert.IsNull(transferredCard.ActualEndTime);
            Assert.AreEqual(2, transferredCard.RecurrenceInterval);
            Assert.AreEqual(RecurrenceUnit.Week, transferredCard.RecurrenceUnit);
            Assert.HasCount(1, transferredCard.CardLabels);
            Assert.AreEqual(1, await db.KanbanCardLabels.CountAsync());
            Assert.IsFalse(await db.KanbanCardComments.AnyAsync());
            var transferredSubscriberId = await db.KanbanCardSubscriptions
                .Where(subscription => subscription.CardId == transferredCard.Id)
                .Select(subscription => subscription.UserId)
                .SingleAsync();
            Assert.AreEqual(sourceOwnerId, transferredSubscriberId);
        }

        var transferredCardResponse = await Http.GetAsync(
            $"/Cards/{transferResult.Id}?returnBoardId={targetBoardId}");
        Assert.AreEqual(HttpStatusCode.OK, transferredCardResponse.StatusCode);

        var originalCardResponse = await Http.GetAsync(
            $"/Cards/{cardId}?returnBoardId={sourceBoardId}");
        Assert.AreEqual(HttpStatusCode.NotFound, originalCardResponse.StatusCode);
    }

    [TestMethod]
    public async Task TransferCard_DoesNotNotifyOriginalAssigneeWithoutTargetBoardAccess()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var assigneeId = await RegisterUserAndGetIdAsync();
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
            var card = new KanbanCard
            {
                Title = "Notify assignee",
                ColumnId = sourceColumnId,
                CreatorUserId = sourceOwnerId,
                AssignedUserId = assigneeId
            };
            db.KanbanCards.Add(card);
            await db.SaveChangesAsync();
            db.KanbanCardSubscriptions.Add(new KanbanCardSubscription
            {
                CardId = card.Id,
                UserId = assigneeId
            });
            await db.SaveChangesAsync();
            cardId = card.Id;
        }

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse(await verificationDb.Notifications.AnyAsync(notification =>
            notification.UserId == assigneeId &&
            notification.Type == NotificationType.CardTransferred));
        Assert.IsFalse(await verificationDb.KanbanCardSubscriptions.AnyAsync(subscription =>
            subscription.UserId == assigneeId));
    }

    [TestMethod]
    public async Task TransferCard_DoesNotNotifyOriginalCreatorWithoutTargetBoardAccess()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var originalCreatorId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var targetOwnerId = await RegisterUserAndGetIdAsync();
        var targetBoardId = await CreateBoardWithOwner(targetOwnerId, "Target Board");
        await CreateShare(targetBoardId, sourceOwnerId, null, SharePermission.Editable);
        await LogoutAsync();
        await LoginAsync(sourceOwnerEmail, sourceOwnerPassword);

        var (cardId, targetColumnId) = await CreateTransferCard(
            sourceBoardId,
            targetBoardId,
            "Notify creator",
            originalCreatorId,
            null);

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);

        Assert.IsFalse(await HasNotification(originalCreatorId, NotificationType.CardTransferred));
    }

    [TestMethod]
    public async Task TransferCard_DropsOriginalAssigneeSubscriptionWithTargetBoardUserShare()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var assigneeId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var targetOwnerId = await RegisterUserAndGetIdAsync();
        var targetBoardId = await CreateBoardWithOwner(targetOwnerId, "Target Board");
        await CreateShare(targetBoardId, sourceOwnerId, null, SharePermission.Editable);
        await CreateShare(targetBoardId, assigneeId, null, SharePermission.ReadOnly);
        await LogoutAsync();
        await LoginAsync(sourceOwnerEmail, sourceOwnerPassword);

        var (cardId, targetColumnId) = await CreateTransferCard(
            sourceBoardId,
            targetBoardId,
            "Notify shared assignee",
            sourceOwnerId,
            assigneeId);

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);

        Assert.IsFalse(await HasNotification(assigneeId, NotificationType.CardTransferred));
        Assert.IsFalse(await HasSubscriptionForTitle("Notify shared assignee", assigneeId));
    }

    [TestMethod]
    public async Task TransferCard_DropsOriginalAssigneeSubscriptionWithTargetBoardRoleShare()
    {
        var (sourceOwnerEmail, sourceOwnerPassword) = await RegisterAndLoginAsync();
        var sourceOwnerId = await GetUserIdByEmailAsync(sourceOwnerEmail);
        var sourceBoardId = await CreateBoardWithOwner(sourceOwnerId, "Source Board");
        await LogoutAsync();

        var assigneeId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var targetOwnerId = await RegisterUserAndGetIdAsync();
        var targetBoardId = await CreateBoardWithOwner(targetOwnerId, "Target Board");
        var roleId = await CreateRoleWithUser("reviewers", assigneeId);
        await CreateShare(targetBoardId, sourceOwnerId, null, SharePermission.Editable);
        await CreateShare(targetBoardId, null, roleId, SharePermission.ReadOnly);
        await LogoutAsync();
        await LoginAsync(sourceOwnerEmail, sourceOwnerPassword);

        var (cardId, targetColumnId) = await CreateTransferCard(
            sourceBoardId,
            targetBoardId,
            "Notify role assignee",
            sourceOwnerId,
            assigneeId);

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, transferResponse.StatusCode);

        Assert.IsFalse(await HasNotification(assigneeId, NotificationType.CardTransferred));
        Assert.IsFalse(await HasSubscriptionForTitle("Notify role assignee", assigneeId));
    }

    [TestMethod]
    public async Task RemovingUsersOnlySharedRole_RemovesTheirCardSubscriptions()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Role subscription board");
        var cardId = await CreateCard(boardId, "Role subscription card");
        await LogoutAsync();

        var subscriberId = await RegisterUserAndGetIdAsync();
        var roleName = "subscription-reviewers-" + Guid.NewGuid();
        var roleId = await CreateRoleWithUser(roleName, subscriberId);
        await CreateShare(boardId, null, roleId, SharePermission.ReadOnly);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.KanbanCardSubscriptions.Add(new KanbanCardSubscription
            {
                CardId = cardId,
                UserId = subscriberId
            });
            await db.SaveChangesAsync();
        }

        await LoginAsAdmin();
        var response = await PostForm($"/Users/ManageRoles/{subscriberId}", new Dictionary<string, string>
        {
            { "id", subscriberId }
        });
        AssertRedirect(response, "/Users/Details/", exact: false);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse(await verificationDb.KanbanCardSubscriptions.AnyAsync(subscription =>
            subscription.CardId == cardId && subscription.UserId == subscriberId));
    }

    [TestMethod]
    public async Task NotificationsIndex_BoardSharedWithoutCard_ReturnsOk()
    {
        var ownerId = await RegisterUserAndGetIdAsync();
        var boardId = await CreateBoardWithOwner(ownerId, "Shared notification board");
        await LogoutAsync();

        var (viewerEmail, viewerPassword) = await RegisterAndLoginAsync();
        var viewerId = await GetUserIdByEmailAsync(viewerEmail);
        await LogoutAsync();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.Notifications.Add(new Notification
            {
                BoardId = boardId,
                UserId = viewerId,
                ActorUserId = ownerId,
                Type = NotificationType.BoardShared
            });
            await db.SaveChangesAsync();
        }

        await LoginAsync(viewerEmail, viewerPassword);
        var response = await Http.GetAsync("/Notifications/Index");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("shared a board with you", content);
        Assert.Contains($"/Kanban/Index?boardId={boardId}", content);
        Assert.Contains("Open Board", content);
    }

    [TestMethod]
    public async Task RemoveShare_RemovesSubscriptionsForUserWhoLosesReadAccess()
    {
        var (ownerEmail, ownerPassword) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var boardId = await CreateBoardWithOwner(ownerId, "Private shared board");
        await LogoutAsync();
        var subscriberId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();
        await CreateShare(boardId, subscriberId, null, SharePermission.ReadOnly);
        var cardId = await CreateCard(boardId, "Subscribed card");
        await AddSubscription(cardId, subscriberId);

        await LoginAsync(ownerEmail, ownerPassword);
        Guid shareId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            shareId = await db.BoardShares
                .Where(share => share.BoardId == boardId && share.SharedWithUserId == subscriberId)
                .Select(share => share.Id)
                .SingleAsync();
        }

        var response = await PostForm($"/Kanban/RemoveShare?id={shareId}", new Dictionary<string, string>());
        AssertRedirect(response, "/Kanban/ManageShares", exact: false);
        Assert.IsFalse(await HasSubscription(cardId, subscriberId));
    }

    [TestMethod]
    public async Task MakingPublicBoardPrivate_RemovesSubscriptionsForUsersWithoutAnotherGrant()
    {
        var (ownerEmail, ownerPassword) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var boardId = await CreateBoardWithOwner(ownerId, "Public board", isPublic: true);
        await LogoutAsync();
        var subscriberId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();
        await LoginAsync(ownerEmail, ownerPassword);
        var cardId = await CreateCard(boardId, "Public subscribed card");
        await AddSubscription(cardId, subscriberId);

        var response = await PostForm(
            $"/Kanban/UpdateVisibility?id={boardId}&publicAccess=false",
            new Dictionary<string, string>());
        AssertRedirect(response, "/Kanban/ManageShares", exact: false);
        Assert.IsFalse(await HasSubscription(cardId, subscriberId));
    }

    [TestMethod]
    public async Task MarkAsRead_CannotMarkAnotherUsersNotification()
    {
        var actorId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var otherUserId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var (currentEmail, currentPassword) = await RegisterAndLoginAsync();
        var currentUserId = await GetUserIdByEmailAsync(currentEmail);
        await LogoutAsync();

        var otherNotificationId = await CreateNotification(otherUserId, actorId, NotificationType.BoardShared);
        await CreateNotification(currentUserId, actorId, NotificationType.BoardShared);
        await LoginAsync(currentEmail, currentPassword);

        var response = await PostForm(
            $"/Notifications/MarkAsRead?id={otherNotificationId}",
            new Dictionary<string, string>(),
            "/Manage/ChangePassword");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var otherNotification = await verificationDb.Notifications.FindAsync(otherNotificationId);
        Assert.IsNotNull(otherNotification);
        Assert.IsFalse(otherNotification.IsRead);
    }

    [TestMethod]
    public async Task MarkAllAsRead_OnlyMarksCurrentUsersNotifications()
    {
        var actorId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var otherUserId = await RegisterUserAndGetIdAsync();
        await LogoutAsync();

        var (currentEmail, currentPassword) = await RegisterAndLoginAsync();
        var currentUserId = await GetUserIdByEmailAsync(currentEmail);
        await LogoutAsync();

        var otherNotificationId = await CreateNotification(otherUserId, actorId, NotificationType.BoardShared);
        var currentNotificationId = await CreateNotification(currentUserId, actorId, NotificationType.BoardShared);
        await LoginAsync(currentEmail, currentPassword);

        var response = await PostForm(
            "/Notifications/MarkAllAsRead",
            new Dictionary<string, string>(),
            "/Manage/ChangePassword");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = Server!.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var otherNotification = await verificationDb.Notifications.FindAsync(otherNotificationId);
        var currentNotification = await verificationDb.Notifications.FindAsync(currentNotificationId);
        Assert.IsNotNull(otherNotification);
        Assert.IsNotNull(currentNotification);
        Assert.IsFalse(otherNotification.IsRead);
        Assert.IsTrue(currentNotification.IsRead);
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

        var transferResponse = await PostForm(
            $"/Kanban/TransferCard?cardId={cardId}&targetBoardId={targetBoardId}&targetColumnId={targetColumnId}",
            new Dictionary<string, string>());
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

    private async Task<int> CreateCard(int boardId, string title)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var columnId = await db.KanbanColumns
            .Where(column => column.BoardId == boardId)
            .OrderBy(column => column.Order)
            .Select(column => column.Id)
            .FirstAsync();
        var card = new KanbanCard { Title = title, ColumnId = columnId };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    private async Task AddSubscription(int cardId, string userId)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        db.KanbanCardSubscriptions.Add(new KanbanCardSubscription { CardId = cardId, UserId = userId });
        await db.SaveChangesAsync();
    }

    private async Task<bool> HasSubscription(int cardId, string userId)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        return await db.KanbanCardSubscriptions.AnyAsync(subscription =>
            subscription.CardId == cardId && subscription.UserId == userId);
    }

    private async Task<bool> HasSubscriptionForTitle(string cardTitle, string userId)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        return await db.KanbanCardSubscriptions.AnyAsync(subscription =>
            subscription.Card.Title == cardTitle && subscription.UserId == userId);
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

    private async Task<(int cardId, int targetColumnId)> CreateTransferCard(
        int sourceBoardId,
        int targetBoardId,
        string title,
        string? creatorUserId,
        string? assignedUserId)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var sourceColumnId = await db.KanbanColumns
            .Where(column => column.BoardId == sourceBoardId)
            .OrderBy(column => column.Order)
            .Select(column => column.Id)
            .FirstAsync();
        var targetColumnId = await db.KanbanColumns
            .Where(column => column.BoardId == targetBoardId)
            .OrderBy(column => column.Order)
            .Select(column => column.Id)
            .FirstAsync();
        var card = new KanbanCard
        {
            Title = title,
            ColumnId = sourceColumnId,
            CreatorUserId = creatorUserId,
            AssignedUserId = assignedUserId
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        db.KanbanCardSubscriptions.AddRange(new[] { creatorUserId, assignedUserId }
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct()
            .Select(userId => new KanbanCardSubscription
            {
                CardId = card.Id,
                UserId = userId!
            }));
        await db.SaveChangesAsync();
        return (card.Id, targetColumnId);
    }

    private async Task<bool> HasNotification(string userId, NotificationType type)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        return await db.Notifications.AnyAsync(notification =>
            notification.UserId == userId &&
            notification.Type == type);
    }

    private async Task<int> CreateNotification(string userId, string actorUserId, NotificationType type)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var notification = new Notification
        {
            UserId = userId,
            ActorUserId = actorUserId,
            Type = type
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private async Task<string> CreateRoleWithUser(string roleName, string userId)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var role = new IdentityRole(roleName)
        {
            NormalizedName = roleName.ToUpperInvariant()
        };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<string>
        {
            RoleId = role.Id,
            UserId = userId
        });
        await db.SaveChangesAsync();
        return role.Id;
    }
}
