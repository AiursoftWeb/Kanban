using System.Net;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Kanban.Authorization;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class PriorityLabelsAssigneeTests : TestBase
{
    [TestMethod]
    public async Task UpdateCardPriority_SetsPriorityOnCard()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var (_, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(ownerId, "Priority Board");
        var cardId = await CreateCardAsync(todoColumnId, "Ship release");

        var response = await PostAsync($"/Kanban/UpdateCardPriority?cardId={cardId}&priority={(int)Priority.Urgent}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual((int)Priority.Urgent, doc.RootElement.GetProperty("Priority").GetInt32());

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var card = await db.KanbanCards.FindAsync(cardId);
        Assert.IsNotNull(card);
        Assert.AreEqual(Priority.Urgent, card.Priority);
    }

    [TestMethod]
    public async Task AssignCard_AssignsSharedUserToCard()
    {
        var (ownerEmail, ownerPassword) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var (boardId, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(ownerId, "Assignment Board");
        var cardId = await CreateCardAsync(todoColumnId, "Review PR");
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        await CreateShareAsync(boardId, assigneeId, SharePermission.ReadOnly);
        await LoginAsync(ownerEmail, ownerPassword);

        var response = await PostAsync($"/Kanban/AssignCard?cardId={cardId}&assignedUserId={Uri.EscapeDataString(assigneeId)}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(assigneeId, doc.RootElement.GetProperty("AssignedUserId").GetString());

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var card = await db.KanbanCards.FindAsync(cardId);
        Assert.IsNotNull(card);
        Assert.AreEqual(assigneeId, card.AssignedUserId);
    }

    [TestMethod]
    public async Task BoardMembers_IncludesUsersFromSharedRole()
    {
        var (ownerEmail, ownerPassword) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var (boardId, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(ownerId, "Role Assignment Board");
        var cardId = await CreateCardAsync(todoColumnId, "Review role member access");
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        var roleId = await CreateRoleWithUserAsync("employees", assigneeId);
        await CreateRoleShareAsync(boardId, roleId, SharePermission.Editable);
        await LoginAsync(ownerEmail, ownerPassword);

        var membersResponse = await Http.GetAsync($"/Kanban/GetBoardMembers?boardId={boardId}");
        membersResponse.EnsureSuccessStatusCode();

        var membersJson = await membersResponse.Content.ReadAsStringAsync();
        using var membersDoc = JsonDocument.Parse(membersJson);
        Assert.IsTrue(membersDoc.RootElement.EnumerateArray().Any(member =>
            member.GetProperty("Id").GetString() == assigneeId));

        var assignResponse = await PostAsync($"/Kanban/AssignCard?cardId={cardId}&assignedUserId={Uri.EscapeDataString(assigneeId)}");
        assignResponse.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task AddAndRemoveLabel_UpdatesCardLabels()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        var (_, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(ownerId, "Label Board");
        var cardId = await CreateCardAsync(todoColumnId, "Fix bug");

        var addResponse = await PostAsync($"/Kanban/AddLabel?cardId={cardId}&name=Bug");
        addResponse.EnsureSuccessStatusCode();

        var addJson = await addResponse.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        var labelId = addDoc.RootElement.GetProperty("Id").GetInt32();
        Assert.IsTrue(addDoc.RootElement.GetProperty("Color").GetString()!.StartsWith('#'));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var cardLabels = await db.KanbanCardLabels.Where(link => link.CardId == cardId).ToListAsync();
            Assert.AreEqual(1, cardLabels.Count);
            Assert.AreEqual(labelId, cardLabels[0].LabelId);
        }

        var removeResponse = await PostAsync($"/Kanban/RemoveLabel?cardId={cardId}&labelId={labelId}");
        removeResponse.EnsureSuccessStatusCode();

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.IsFalse(await db.KanbanCardLabels.AnyAsync(link => link.CardId == cardId && link.LabelId == labelId));
            Assert.IsTrue(await db.KanbanLabels.AnyAsync(label => label.Id == labelId));
        }
    }

    [TestMethod]
    public async Task MyTasks_DefaultPage_ShowsOnlyIncompleteAssignedCards()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        var (boardId, todoColumnId, progressColumnId, completedColumnId) = await CreateBoardWithStatusesAsync(ownerId, "My Tasks Board");
        await CreateShareAsync(boardId, assigneeId, SharePermission.ReadOnly);
        await CreateCardAsync(todoColumnId, "Plan sprint", assigneeId, Priority.High);
        await CreateCardAsync(progressColumnId, "Implement API", assigneeId, Priority.Medium);
        await CreateCardAsync(completedColumnId, "Write retrospective", assigneeId, Priority.Low);

        await LoginAsync(assigneeEmail, "Test-Password-123");
        var response = await Http.GetAsync("/MyTasks/Index");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Plan sprint", html);
        Assert.Contains("Implement API", html);
        Assert.DoesNotContain("Write retrospective", html);
    }

    [TestMethod]
    public async Task MyTasks_FilteringSupportsStatusAndLabelModes()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        var (boardId, todoColumnId, progressColumnId, _) = await CreateBoardWithStatusesAsync(ownerId, "Filter Board");
        await CreateShareAsync(boardId, assigneeId, SharePermission.ReadOnly);

        var backendLabel = await CreateLabelAsync("Backend", "#3B82F6");
        var bugLabel = await CreateLabelAsync("Bug", "#EF4444");
        var featureLabel = await CreateLabelAsync("Feature", "#22C55E");

        var backendBugCardId = await CreateCardAsync(progressColumnId, "Fix API timeout", assigneeId, Priority.Urgent);
        await AddLabelToCardAsync(backendBugCardId, backendLabel);
        await AddLabelToCardAsync(backendBugCardId, bugLabel);

        var featureCardId = await CreateCardAsync(todoColumnId, "Ship onboarding", assigneeId, Priority.High);
        await AddLabelToCardAsync(featureCardId, featureLabel);

        var bugOnlyCardId = await CreateCardAsync(todoColumnId, "Triage issue", assigneeId, Priority.Medium);
        await AddLabelToCardAsync(bugOnlyCardId, bugLabel);

        await LoginAsync(assigneeEmail, "Test-Password-123");

        var inProgressResponse = await Http.GetAsync("/MyTasks/Index?status=in-progress");
        inProgressResponse.EnsureSuccessStatusCode();
        var inProgressHtml = await inProgressResponse.Content.ReadAsStringAsync();
        Assert.Contains("Fix API timeout", inProgressHtml);
        Assert.DoesNotContain("Ship onboarding", inProgressHtml);
        Assert.DoesNotContain("Triage issue", inProgressHtml);

        var allLabelsResponse = await Http.GetAsync($"/MyTasks/Index?status=all&labelIds={bugLabel.Id},{backendLabel.Id}&labelMode=all");
        allLabelsResponse.EnsureSuccessStatusCode();
        var allLabelsHtml = await allLabelsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Fix API timeout", allLabelsHtml);
        Assert.DoesNotContain("Ship onboarding", allLabelsHtml);
        Assert.DoesNotContain("Triage issue", allLabelsHtml);

        var anyLabelsResponse = await Http.GetAsync($"/MyTasks/Index?status=all&labelIds={bugLabel.Id},{featureLabel.Id}&labelMode=any");
        anyLabelsResponse.EnsureSuccessStatusCode();
        var anyLabelsHtml = await anyLabelsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Fix API timeout", anyLabelsHtml);
        Assert.Contains("Ship onboarding", anyLabelsHtml);
        Assert.Contains("Triage issue", anyLabelsHtml);
    }

    [TestMethod]
    public async Task MyTasks_NotStartedFilter_ShowsOnlyNotStartedCards()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        var (boardId, todoColumnId, progressColumnId, completedColumnId) = await CreateBoardWithStatusesAsync(ownerId, "Not Started Board");
        await CreateShareAsync(boardId, assigneeId, SharePermission.ReadOnly);
        await CreateCardAsync(todoColumnId, "Plan sprint", assigneeId, Priority.High);
        await CreateCardAsync(progressColumnId, "Implement API", assigneeId, Priority.Medium);
        await CreateCardAsync(completedColumnId, "Write retrospective", assigneeId, Priority.Low);

        await LoginAsync(assigneeEmail, "Test-Password-123");
        var response = await Http.GetAsync("/MyTasks/Index?status=not-started");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Plan sprint", html);
        Assert.DoesNotContain("Implement API", html);
        Assert.DoesNotContain("Write retrospective", html);
    }

    [TestMethod]
    public async Task MyTasks_LabelToggle_VisualClassesAppliedCorrectly()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var ownerId = await GetUserIdByEmailAsync(ownerEmail);
        await LogoutAsync();

        var (assigneeEmail, _) = await RegisterAndLoginAsync();
        var assigneeId = await GetUserIdByEmailAsync(assigneeEmail);
        await LogoutAsync();

        var (boardId, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(ownerId, "Label Toggle Board");
        await CreateShareAsync(boardId, assigneeId, SharePermission.ReadOnly);

        var backendLabel = await CreateLabelAsync("Backend", "#3B82F6");
        var bugLabel = await CreateLabelAsync("Bug", "#EF4444");

        var cardId = await CreateCardAsync(todoColumnId, "Fix API timeout", assigneeId, Priority.Urgent);
        await AddLabelToCardAsync(cardId, backendLabel);
        await AddLabelToCardAsync(cardId, bugLabel);

        await LoginAsync(assigneeEmail, "Test-Password-123");

        // No labels selected - both chips should be inactive
        var noSelectionResponse = await Http.GetAsync("/MyTasks/Index?status=all");
        noSelectionResponse.EnsureSuccessStatusCode();
        var noSelectionHtml = await noSelectionResponse.Content.ReadAsStringAsync();

        // Selected labels should have the "active" CSS class
        var selectedResponse = await Http.GetAsync($"/MyTasks/Index?status=all&labelIds={backendLabel.Id}");
        selectedResponse.EnsureSuccessStatusCode();
        var selectedHtml = await selectedResponse.Content.ReadAsStringAsync();

        // When selected, the chip gets class "active" and color is applied inline
        Assert.Contains("label-filter-chip active", selectedHtml);
        Assert.Contains(backendLabel.Color, selectedHtml);

        // When no label is selected, chips use the default muted style (no inline color)
        Assert.DoesNotContain("label-filter-chip active", noSelectionHtml);
    }

    [TestMethod]
    public async Task MyTasks_ViewOtherUserTasks_WithoutPermission_ReturnsForbid()
    {
        var (managerEmail, _) = await RegisterAndLoginAsync();
        await LogoutAsync();

        var (employeeEmail, _) = await RegisterAndLoginAsync();
        var employeeId = await GetUserIdByEmailAsync(employeeEmail);
        await LogoutAsync();

        await LoginAsync(managerEmail, "Test-Password-123");

        var response = await Http.GetAsync($"/MyTasks/Index?targetUserId={employeeId}");
        Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Found);
    }

    [TestMethod]
    public async Task MyTasks_ViewOtherUserTasks_WithPermission_ReturnsOtherUserTasks()
    {
        var (managerEmail, _) = await RegisterAndLoginAsync();
        var managerId = await GetUserIdByEmailAsync(managerEmail);
        await AssignPermissionToUserAsync(managerId, AppPermissionNames.CanViewAnyUserTasks);
        await LogoutAsync();

        var (employeeEmail, _) = await RegisterAndLoginAsync();
        var employeeId = await GetUserIdByEmailAsync(employeeEmail);
        await LogoutAsync();

        var (_, todoColumnId, _, _) = await CreateBoardWithStatusesAsync(employeeId, "Employee Board");
        await CreateCardAsync(todoColumnId, "Employee Task 1", employeeId, Priority.High);

        await LoginAsync(managerEmail, "Test-Password-123");

        var response = await Http.GetAsync($"/MyTasks/Index?targetUserId={employeeId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Employee Task 1", html);
        Assert.Contains("You are currently viewing tasks assigned to", html);
    }

    private async Task AssignPermissionToUserAsync(string userId, string permissionName)
    {
        using var scope = CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByIdAsync(userId);
        await userManager.AddClaimAsync(user!, new System.Security.Claims.Claim(AppPermissions.Type, permissionName));
    }

    private async Task<string> GetUserIdByEmailAsync(string email)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        return (await db.Users.FirstAsync(user => user.Email == email)).Id;
    }

    private async Task<(int boardId, int todoColumnId, int progressColumnId, int completedColumnId)> CreateBoardWithStatusesAsync(string ownerId, string boardName)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var board = new KanbanBoard
        {
            Name = boardName,
            UserId = ownerId
        };
        var todoColumn = new KanbanColumn { Name = "To Do", Order = 0, Board = board, ColumnStatus = ColumnStatus.NotStarted };
        var progressColumn = new KanbanColumn { Name = "In Progress", Order = 1, Board = board, ColumnStatus = ColumnStatus.InProgress };
        var completedColumn = new KanbanColumn { Name = "Done", Order = 2, Board = board, ColumnStatus = ColumnStatus.Completed };

        db.KanbanBoards.Add(board);
        db.KanbanColumns.AddRange(todoColumn, progressColumn, completedColumn);
        await db.SaveChangesAsync();

        return (board.Id, todoColumn.Id, progressColumn.Id, completedColumn.Id);
    }

    private async Task<int> CreateCardAsync(int columnId, string title, string? assignedUserId = null, Priority priority = Priority.None)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var maxOrder = await db.KanbanCards.Where(card => card.ColumnId == columnId).MaxAsync(card => (int?)card.Order) ?? -1;

        var card = new KanbanCard
        {
            Title = title,
            ColumnId = columnId,
            Order = maxOrder + 1,
            AssignedUserId = assignedUserId,
            Priority = priority
        };

        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    private async Task CreateShareAsync(int boardId, string userId, SharePermission permission)
    {
        using var scope = CreateScope();
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

    private async Task<string> CreateRoleWithUserAsync(string roleName, string userId)
    {
        using var scope = CreateScope();
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

    private async Task CreateRoleShareAsync(int boardId, string roleId, SharePermission permission)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            SharedWithRoleId = roleId,
            Permission = permission
        });
        await db.SaveChangesAsync();
    }

    private async Task<KanbanLabel> CreateLabelAsync(string name, string color)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var label = new KanbanLabel
        {
            Name = name,
            Color = color
        };
        db.KanbanLabels.Add(label);
        await db.SaveChangesAsync();
        return label;
    }

    private async Task AddLabelToCardAsync(int cardId, KanbanLabel label)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        db.KanbanCardLabels.Add(new KanbanCardLabel
        {
            CardId = cardId,
            LabelId = label.Id
        });
        await db.SaveChangesAsync();
    }

    private IServiceScope CreateScope()
    {
        return Server!.Services.CreateScope();
    }

    private async Task<HttpResponseMessage> PostAsync(string url)
    {
        return await Http.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>()));
    }
}
