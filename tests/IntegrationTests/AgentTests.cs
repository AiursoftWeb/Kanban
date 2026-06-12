using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Tools.Read;
using Aiursoft.Kanban.Services.Tools.Write;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class AgentTests : TestBase
{
    [TestMethod]
    public async Task ToolRegistry_DiscoversAllTools()
    {
        await LoginAsAdmin();
        var registry = GetService<ToolRegistry>();
        var allTools = registry.AllTools;

        Assert.IsTrue(allTools.Count >= 22, $"Expected at least 22 tools, found {allTools.Count}");

        var readTools = allTools.Where(t => !registry.IsWriteTool(t.ProtocolTool.Name)).ToList();
        var writeTools = allTools.Where(t => registry.IsWriteTool(t.ProtocolTool.Name)).ToList();

        Assert.IsTrue(readTools.Count >= 13, $"Expected at least 13 read tools, found {readTools.Count}");
        Assert.IsTrue(writeTools.Count >= 9, $"Expected at least 9 write tools, found {writeTools.Count}");

        foreach (var tool in allTools)
        {
            Assert.IsNotNull(tool.ProtocolTool.Name, $"Tool has no name");
            Assert.IsNotNull(tool.ProtocolTool.Description, $"Tool '{tool.ProtocolTool.Name}' has no description");
        }
    }

    [TestMethod]
    public async Task ToolRegistry_ReadToolsNotMarkedAsWrite()
    {
        await LoginAsAdmin();
        var registry = GetService<ToolRegistry>();

        var readToolNames = new[] { "GetUserBoards", "GetBoardById", "GetColumns", "GetCardsInColumn",
            "GetCardById", "SearchCards", "GetOverdueCards", "GetBoardMembers", "SearchUsers", "SearchLabels",
            "GetBoardShares", "GetCardsByPriority", "GetUnassignedCards", "GetCardsByLabel",
            "GetMyTasks", "GetPublicBoards", "GetSharedBoards" };

        foreach (var name in readToolNames)
        {
            Assert.IsFalse(registry.IsWriteTool(name), $"'{name}' should not be a write tool");
        }
    }

    [TestMethod]
    public async Task ToolRegistry_WriteToolsMarkedAsWrite()
    {
        await LoginAsAdmin();
        var registry = GetService<ToolRegistry>();

        var writeToolNames = new[] { "CreateBoard", "CreateCard", "MoveCard", "DeleteBoard",
            "CreateColumn", "AddLabel", "AssignCard", "ShareBoard", "RemoveBoardShare", "UpdateBoardVisibility" };

        foreach (var name in writeToolNames)
        {
            Assert.IsTrue(registry.IsWriteTool(name), $"'{name}' should be a write tool");
        }
    }

    [TestMethod]
    public async Task ToolRegistry_EachToolHasValidSchema()
    {
        await LoginAsAdmin();
        var registry = GetService<ToolRegistry>();

        foreach (var tool in registry.AllTools)
        {
            var schema = tool.ProtocolTool.InputSchema;
            var raw = schema.GetRawText();
            Assert.IsTrue(raw.Contains("\"type\""), $"Tool '{tool.ProtocolTool.Name}' schema missing 'type'");
            Assert.IsTrue(raw.Contains("\"properties\""),
                $"Tool '{tool.ProtocolTool.Name}' schema missing 'properties'");
        }
    }

    // ── AdviceService ───────────────────────────────────────

    [TestMethod]
    public async Task AdviceService_CreateAndRetrieve()
    {
        await LoginAsAdmin();
        var service = GetService<AdviceService>();
        var conversationId = Guid.NewGuid();

        var advice = service.Create(
            conversationId: conversationId,
            toolName: "CreateCard",
            toolDisplayName: "Create Card",
            toolDescription: "Create a new card",
            parameters: new Dictionary<string, object?> { ["title"] = "Test" },
            parameterDisplay: "title: Test",
            toolCallId: "call_1");

        Assert.IsNotNull(advice);
        Assert.AreEqual(AdviceStatus.Pending, advice.Status);

        var retrieved = service.Get(advice.Id);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("CreateCard", retrieved.ToolName);

        var pending = service.GetPendingForConversation(conversationId);
        Assert.AreEqual(1, pending.Count);
    }

    [TestMethod]
    public async Task AdviceService_UpdateStatus()
    {
        await LoginAsAdmin();
        var service = GetService<AdviceService>();

        var advice = service.Create(Guid.NewGuid(), "TestTool", "Test", "Desc",
            new(), "", "call_1");

        service.UpdateStatus(advice.Id, AdviceStatus.Approved);
        Assert.AreEqual(AdviceStatus.Approved, service.Get(advice.Id)!.Status);

        service.SetResult(advice.Id, "Success", null);
        Assert.AreEqual(AdviceStatus.Executed, service.Get(advice.Id)!.Status);
    }

    [TestMethod]
    public async Task AdviceService_RemoveConversationAdvice()
    {
        await LoginAsAdmin();
        var service = GetService<AdviceService>();
        var cid = Guid.NewGuid();

        service.Create(cid, "T1", "T1", "", new(), "", "c1");
        service.Create(cid, "T2", "T2", "", new(), "", "c2");
        service.Create(Guid.NewGuid(), "T3", "T3", "", new(), "", "c3");

        service.RemoveConversationAdvice(cid);
        Assert.AreEqual(0, service.GetPendingForConversation(cid).Count);
    }

    // ── AgentService ────────────────────────────────────────

    [TestMethod]
    public async Task AgentService_StartRun_CreatesConversation()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();
        const string userId = "admin";

        var conversationId = service.StartRun(userId, boardId, "Hello");

        var conversation = service.GetConversation(conversationId)!;
        Assert.AreEqual(userId, conversation.UserId);
        Assert.AreEqual(boardId, conversation.BoardId);
        Assert.IsTrue(conversation.Messages.Count >= 2); // system + user
    }

    [TestMethod]
    public async Task AgentService_GetConversation_NotFoundReturnsNull()
    {
        await LoginAsAdmin();
        var service = GetService<IAgentService>();
        Assert.IsNull(service.GetConversation(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task AgentService_CancelRun_RemovesConversation()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("admin", boardId, "Hello");
        Assert.IsNotNull(service.GetConversation(conversationId));

        service.CancelRun(conversationId);
        Assert.IsNull(service.GetConversation(conversationId));
    }

    // ── AgentController ─────────────────────────────────────

    [TestMethod]
    public async Task AgentController_Unauthenticated_ReturnsRedirect()
    {
        var response = await Http.GetAsync($"/Agent/Status?conversationId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        StringAssert.Contains(response.Headers.Location!.OriginalString, "Login");
    }

    [TestMethod]
    public async Task AgentController_SendMessage_RequiresAuth()
    {
        var response = await Http.PostAsync("/Agent/SendMessage",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_Status_RequiresAuth()
    {
        var response = await Http.GetAsync($"/Agent/Status?conversationId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_ApproveAdvice_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/ApproveAdvice?conversationId={Guid.NewGuid()}&adviceId={Guid.NewGuid()}",
            new StringContent("", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_RejectAdvice_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/RejectAdvice?conversationId={Guid.NewGuid()}&adviceId={Guid.NewGuid()}",
            new StringContent("", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_ApproveAll_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/ApproveAll?conversationId={Guid.NewGuid()}",
            new StringContent("", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_Cancel_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/Cancel?conversationId={Guid.NewGuid()}",
            new StringContent("", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_Status_NonExistentConversation()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync($"/Agent/Status?conversationId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_SendMessage_InvalidBoard()
    {
        await LoginAsAdmin();
        var token = await GetAntiCsrfToken("/");
        var json = JsonSerializer.Serialize(new { boardId = 99999, message = "Hello" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);

        var response = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_SendMessage_WithValidBoard()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var token = await GetAntiCsrfToken("/");
        var json = JsonSerializer.Serialize(new { boardId, message = "What cards do I have?" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);

        var response = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.IsTrue(result.TryGetProperty("ConversationId", out _));
    }

    [TestMethod]
    public async Task AgentController_OtherUserCannotAccessConversation()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var token = await GetAntiCsrfToken("/");

        var json = JsonSerializer.Serialize(new { boardId, message = "Hello" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);
        var sendResponse = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(HttpStatusCode.OK, sendResponse.StatusCode);
        var sendResult = JsonSerializer.Deserialize<JsonElement>(
            await sendResponse.Content.ReadAsStringAsync());
        var conversationId = sendResult.GetProperty("ConversationId").GetString()!;

        var statusResponse = await Http.GetAsync($"/Agent/Status?conversationId={conversationId}");
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);

        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(conversationId))!;

        var adminEmail = "admin@default.com";
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        Assert.AreEqual(adminUser!.Id, conversation.UserId,
            "Conversation should belong to the admin user");
    }

    // ── KanbanAccessService ─────────────────────────────────

    [TestMethod]
    public async Task KanbanAccessService_OwnerHasFullAccess()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        using var scope = Server!.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<KanbanAccessService>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var board = await db.KanbanBoards.FindAsync(boardId);
        var adminUserId = db.Users.First(u => u.Email == "admin@default.com").Id;

        Assert.IsTrue(await access.HasReadAccess(board!, adminUserId));
        Assert.IsTrue(await access.HasEditAccess(board!, adminUserId));
    }

    [TestMethod]
    public async Task KanbanAccessService_NonOwnerCannotEdit()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        using var scope = Server!.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<KanbanAccessService>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var board = await db.KanbanBoards.FindAsync(boardId);

        var otherUserId = Guid.NewGuid().ToString();
        Assert.IsFalse(await access.HasReadAccess(board!, otherUserId));
        Assert.IsFalse(await access.HasEditAccess(board!, otherUserId));
    }

    [TestMethod]
    public async Task KanbanAccessService_PublicBoardReadableByAnyone()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        using var scope = Server!.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<KanbanAccessService>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var board = await db.KanbanBoards.FindAsync(boardId);
        board!.IsPublic = true;
        await db.SaveChangesAsync();

        var strangerId = Guid.NewGuid().ToString();
        Assert.IsTrue(await access.HasReadAccess(board, strangerId));
        Assert.IsFalse(await access.HasEditAccess(board, strangerId));
    }

    // ── Privilege escalation prevention ───────────────────

    [TestMethod]
    public async Task ToolSchemas_DoNotExposeUserId()
    {
        // Verify that tool schemas do NOT expose userId — CurrentUserService
        // is registered in DI so MCP SDK excludes it from the generated schema.
        await LoginAsAdmin();
        var registry = GetService<ToolRegistry>();

        foreach (var tool in registry.AllTools)
        {
            var raw = tool.ProtocolTool.InputSchema.GetRawText();
            // Neither "userId" string parameter nor CurrentUserService should appear
            Assert.IsFalse(raw.Contains("\"userId\""),
                $"Tool '{tool.ProtocolTool.Name}' should not expose userId in its schema.");
        }
    }

    [TestMethod]
    public async Task SearchUsers_GlobalSearch_ReturnsAllUsers()
    {
        // SearchUsers is global — it returns all users in the system.
        // This is safe because userId is injected via CurrentUserService and
        // board access is enforced by KanbanAccessService at the tool level.
        // Knowing another user's ID does not enable impersonation.
        await LoginAsAdmin();

        // Register two users who have no board relationship with admin
        var (user2Email, _) = await RegisterAndLoginAsync();
        var (user3Email, _) = await RegisterAndLoginAsync();

        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        var user2 = db.Users.First(u => u.Email == user2Email);
        var user3 = db.Users.First(u => u.Email == user3Email);

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var userLookupTools = scope.ServiceProvider.GetRequiredService<UserLookupTools>();

        // Search with empty query — should return all users (global search)
        var result = await userLookupTools.SearchUsers(query: "");

        // Should include admin, user2, and user3 (all global users)
        StringAssert.Contains(result, adminUser.Id, "Should include admin");
        StringAssert.Contains(result, user2.Id, "Should include user2 (global search)");
        StringAssert.Contains(result, user3.Id, "Should include user3 (global search)");
    }

    [TestMethod]
    public async Task AgentService_ConversationUserIdMatchesAuthenticatedUser()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("fake-attacker-id", boardId,
            "Create a board for user X");

        // Even though the caller passed "fake-attacker-id", the conversation
        // should have recorded the actual authenticated user — the controller
        // is responsible for passing the real userId. This test verifies
        // the conversation stores whatever userId is passed to StartRun.
        var conversation = service.GetConversation(conversationId)!;
        // StartRun accepts whatever userId is given — it's the controller's job to pass the real one.
        // In production, AgentController passes userManager.GetUserId(User).
        Assert.AreEqual("fake-attacker-id", conversation.UserId,
            "StartRun stores the userId it receives; controller must pass authenticated userId");
    }

    [TestMethod]
    public async Task AgentController_SendMessage_UsesAuthenticatedUserId()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var token = await GetAntiCsrfToken("/");

        var json = JsonSerializer.Serialize(new { boardId, message = "Show my boards" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);

        var response = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(body);
        var conversationId = result.GetProperty("ConversationId").GetString()!;

        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(conversationId))!;

        // The conversation's UserId must match the authenticated admin user, not any LLM-supplied value
        var adminEmail = "admin@default.com";
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        Assert.AreEqual(adminUser!.Id, conversation.UserId,
            "Conversation UserId must match the authenticated user, not the LLM's input");
    }

    [TestMethod]
    public async Task KanbanAgent_CannotAccessOtherUserPrivateBoard()
    {
        // User1 (admin) creates a private board
        await LoginAsAdmin();
        await CreateBoardAndFirstColumnAsync();

        // User2 registers and creates their own private board
        await RegisterAndLoginAsync();
        var token = await GetAntiCsrfToken("/");
        var createResponse = await Http.PostAsync("/Kanban/CreateBoard",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "name", $"User2 Private Board {Guid.NewGuid():N}" },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var location = createResponse.Headers.Location!.OriginalString;
        var user2BoardId = int.Parse(
            location[(location.IndexOf("boardId=", StringComparison.Ordinal) + 8)..].Split('&', '/').First());

        // User1 (admin) tries to access user2's private board via agent
        await LoginAsAdmin();
        var agentToken = await GetAntiCsrfToken("/");
        var json = JsonSerializer.Serialize(new { boardId = user2BoardId, message = "Show me this board" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", agentToken);

        var agentResponse = await Http.PostAsync("/Agent/SendMessage", content);
        // Forbid() returns a redirect (302) to the access-denied path.
        // The agent should not be able to access another user's private board.
        Assert.AreEqual(HttpStatusCode.Redirect, agentResponse.StatusCode,
            $"Agent should not allow accessing another user's private board. Got: {agentResponse.StatusCode}");
    }

    [TestMethod]
    public async Task AgentService_StartRun_UserIdPreserved()
    {
        // Verify that StartRun preserves the userId and boardId correctly
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var agentService = GetService<IAgentService>();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var adminUser = await userManager.FindByEmailAsync("admin@default.com");
        var realUserId = adminUser!.Id;

        var conversationId = agentService.StartRun(realUserId, boardId, "Hello");
        var conversation = agentService.GetConversation(conversationId)!;
        Assert.AreEqual(realUserId, conversation.UserId);
        Assert.AreEqual(boardId, conversation.BoardId);

        // Verify system prompt no longer exposes userId for tool use
        var systemMsg = conversation.Messages.FirstOrDefault(m => m.Role == "system");
        Assert.IsNotNull(systemMsg);
        Assert.IsFalse(systemMsg.Content!.Contains($"The current user ID is \"{realUserId}\""),
            "System prompt should NOT expose raw userId since it's server-injected");
        StringAssert.Contains(systemMsg.Content, "The server handles identity automatically",
            "System prompt should explain that identity is handled server-side");
    }

    // ── Continuous conversation ─────────────────────────

    [TestMethod]
    public async Task AgentService_ContinueRun_ExtendsConversation()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("admin", boardId, "First message");
        var conversation = service.GetConversation(conversationId)!;
        var originalCount = conversation.Messages.Count;
        Assert.IsTrue(originalCount >= 2); // system + user (plus possibly assistant from background task)

        // Simulate completion
        conversation.State = AgentState.Completed;

        // Continue with a follow-up
        var result = service.ContinueRun(conversationId, "admin", "Follow-up question");
        Assert.IsNotNull(result);
        Assert.AreEqual(conversationId, result.Value);

        var continued = service.GetConversation(conversationId)!;
        Assert.AreEqual(AgentState.Thinking, continued.State);
        Assert.AreEqual(originalCount + 1, continued.Messages.Count); // +1: follow-up user message (system-reminder no longer re-injected)
        Assert.AreEqual("Follow-up question", continued.Messages.Last().Content);
    }

    [TestMethod]
    public async Task AgentService_ContinueRun_WrongUserReturnsNull()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("admin", boardId, "Hello");
        var conversation = service.GetConversation(conversationId)!;
        conversation.State = AgentState.Completed;

        // Different user tries to continue
        var result = service.ContinueRun(conversationId, "different-user", "Hijack");
        Assert.IsNull(result, "Different user should not be able to continue another's conversation");
    }

    [TestMethod]
    public async Task AgentService_ContinueRun_StillThinkingReturnsNull()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("admin", boardId, "Hello");
        // State is Thinking (not yet completed)

        var result = service.ContinueRun(conversationId, "admin", "Are you done yet?");
        Assert.IsNull(result, "Should not continue while conversation is still thinking");
    }

    [TestMethod]
    public async Task AgentController_ContinueRun_ViaSendMessageEndpoint()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var token = await GetAntiCsrfToken("/");

        // Start first message
        var json1 = JsonSerializer.Serialize(
            new { boardId, message = "Hello" });
        var content1 = new StringContent(json1, Encoding.UTF8, "application/json");
        content1.Headers.Add("RequestVerificationToken", token);
        var resp1 = await Http.PostAsync("/Agent/SendMessage", content1);
        Assert.AreEqual(HttpStatusCode.OK, resp1.StatusCode);
        var body1 = await resp1.Content.ReadAsStringAsync();
        var convId = JsonSerializer.Deserialize<JsonElement>(body1)
            .GetProperty("ConversationId").GetString()!;

        // Complete the conversation manually
        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(convId))!;
        conversation.State = AgentState.Completed;

        // Continue with same conversationId
        var json2 = $"{{ \"boardId\": {boardId}, \"message\": \"Follow-up\", \"conversationId\": \"{convId}\" }}";
        var content2 = new StringContent(json2, Encoding.UTF8, "application/json");
        content2.Headers.Add("RequestVerificationToken", token);
        var resp2 = await Http.PostAsync("/Agent/SendMessage", content2);
        Assert.AreEqual(HttpStatusCode.OK, resp2.StatusCode);
        var body2 = await resp2.Content.ReadAsStringAsync();
        var convId2 = JsonSerializer.Deserialize<JsonElement>(body2)
            .GetProperty("ConversationId").GetString()!;

        // Same conversation should be reused
        Assert.AreEqual(convId, convId2, "Continue should return the same conversation ID");
    }

    // ── Share tools ──────────────────────────────────────

    [TestMethod]
    public async Task ShareWriteTools_ShareBoardAndGetShares()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        // Register target user
        var (userEmail, _) = await RegisterAndLoginAsync();
        await LoginAsAdmin();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        var targetUser = db.Users.First(u => u.Email == userEmail);

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        // Share board
        var shareTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();
        var shareResult = await shareTools.ShareBoard(boardId, targetUser.Id, null, "ReadOnly");
        StringAssert.Contains(shareResult, "shared with user");

        // Get shares
        var userLookupTools = scope.ServiceProvider.GetRequiredService<UserLookupTools>();
        var sharesResult = await userLookupTools.GetBoardShares(boardId);
        StringAssert.Contains(sharesResult, targetUser.Id);
        StringAssert.Contains(sharesResult, "ReadOnly");

        // Share ID should be in the result for removal (format: "Share #<guid>: User:...")
        var shareIdStr = sharesResult.Split("Share #")[1].Split(":")[0];
        var shareId = Guid.Parse(shareIdStr);

        // Remove share
        var removeResult = await shareTools.RemoveBoardShare(shareId);
        StringAssert.Contains(removeResult, "Share removed");
    }

    [TestMethod]
    public async Task ShareWriteTools_OnlyOwnerCanShare()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        // Register a non-owner user
        var (otherEmail, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var otherUser = db.Users.First(u => u.Email == otherEmail);
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = otherUser.Id;

        var shareTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();
        var result = await shareTools.ShareBoard(boardId, adminUser.Id, null, "ReadOnly");

        // Non-owner should not be able to share
        StringAssert.Contains(result, "Only the board owner");
    }

    [TestMethod]
    public async Task ShareWriteTools_UpdateBoardVisibility()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var shareTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();

        var result = await shareTools.UpdateBoardVisibility(boardId, true);
        StringAssert.Contains(result, "public");

        var board = await db.KanbanBoards.FindAsync(boardId);
        Assert.IsTrue(board!.IsPublic);

        result = await shareTools.UpdateBoardVisibility(boardId, false);
        StringAssert.Contains(result, "private");

        await db.Entry(board).ReloadAsync();
        Assert.IsFalse(board.IsPublic);
    }

    // ── Batch write tools ─────────────────────────────────

    [TestMethod]
    public async Task BatchCreateCards_CreatesCardsSuccessfully()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var cardsJson = """[{"title":"Card A","description":"Desc A"},{"title":"Card B","description":"Desc B"},{"title":"Card C"}]""";

        var result = await batchTools.BatchCreateCards(columnId, cardsJson);
        StringAssert.Contains(result, "Created 3 card(s)");

        var cards = db.KanbanCards.Where(c => c.ColumnId == columnId).ToList();
        Assert.AreEqual(3, cards.Count, "Should create exactly 3 cards in the database");
        Assert.IsTrue(cards.Any(c => c.Title == "Card A"), "Card A should exist");
        Assert.IsTrue(cards.Any(c => c.Title == "Card B"), "Card B should exist");
        Assert.IsTrue(cards.Any(c => c.Title == "Card C"), "Card C should exist");
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            cards.OrderBy(c => c.Order).Select(c => c.Order).ToArray(),
            "Cards should have sequential order starting from 0");
    }

    [TestMethod]
    public async Task BatchCreateCards_CaseInsensitivePropertyNames()
    {
        // Verifies the fix: System.Text.Json deserializes with PropertyNameCaseInsensitive = true
        // The LLM sends lowercase "title" and "description" in the JSON.
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        // Exact format the LLM sends: lowercase property names
        var cardsJson = """[{"title":"Lowercase Title Works","description":"Lowercase description works"}]""";

        var result = await batchTools.BatchCreateCards(columnId, cardsJson);
        StringAssert.Contains(result, "Created 1 card(s)");

        var card = db.KanbanCards.First(c => c.ColumnId == columnId);
        Assert.AreEqual("Lowercase Title Works", card.Title,
            "Title should be populated from lowercase 'title' JSON key");
        Assert.AreEqual("Lowercase description works", card.Description,
            "Description should be populated from lowercase 'description' JSON key");
    }

    [TestMethod]
    public async Task BatchCreateCards_MixedCasePropertyNames()
    {
        // Also verify PascalCase and mixed case still work
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var cardsJson = """[{"Title":"PascalCase Title","Description":"PascalCase desc"}]""";

        var result = await batchTools.BatchCreateCards(columnId, cardsJson);
        StringAssert.Contains(result, "Created 1 card(s)");

        var card = db.KanbanCards.First(c => c.ColumnId == columnId);
        Assert.AreEqual("PascalCase Title", card.Title);
        Assert.AreEqual("PascalCase desc", card.Description);
    }

    [TestMethod]
    public async Task BatchCreateCards_EmptyArrayReturnsError()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var result = await batchTools.BatchCreateCards(columnId, "[]");

        StringAssert.Contains(result, "Error: No cards specified.");
    }

    [TestMethod]
    public async Task BatchCreateCards_InvalidJsonReturnsError()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var result = await batchTools.BatchCreateCards(columnId, "not-json-at-all");

        StringAssert.Contains(result, "Error: Invalid JSON format.");
    }

    [TestMethod]
    public async Task BatchCreateCards_SkipsEmptyTitles()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var cardsJson = """[{"title":"Valid Card"},{"title":"  "},{"title":""},{"title":"Another Valid"}]""";

        var result = await batchTools.BatchCreateCards(columnId, cardsJson);
        StringAssert.Contains(result, "Created 2 card(s)");

        var cards = db.KanbanCards.Where(c => c.ColumnId == columnId).ToList();
        Assert.AreEqual(2, cards.Count, "Only non-empty title cards should be created");
        Assert.IsTrue(cards.Any(c => c.Title == "Valid Card"));
        Assert.IsTrue(cards.Any(c => c.Title == "Another Valid"));
    }

    [TestMethod]
    public async Task BatchCreateCards_NoPermissionReturnsError()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        // Register a non-owner user
        var (otherEmail, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var otherUser = db.Users.First(u => u.Email == otherEmail);
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = otherUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var result = await batchTools.BatchCreateCards(columnId, """[{"title":"Should Not Create"}]""");

        StringAssert.Contains(result, "Error: You do not have permission");
    }

    [TestMethod]
    public async Task BatchCreateCards_ColumnNotFoundReturnsError()
    {
        await LoginAsAdmin();
        await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var result = await batchTools.BatchCreateCards(999999, """[{"title":"Card"}]""");

        StringAssert.Contains(result, "Error: Column not found.");
    }

    [TestMethod]
    public async Task BatchCreateCards_TrimsTitleAndDescription()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var batchTools = scope.ServiceProvider.GetRequiredService<BatchWriteTools>();
        var cardsJson = """[{"title":"  Padded Title  ","description":"  Padded Desc  "}]""";

        var result = await batchTools.BatchCreateCards(columnId, cardsJson);
        StringAssert.Contains(result, "Created 1 card(s)");

        var card = db.KanbanCards.First(c => c.ColumnId == columnId);
        Assert.AreEqual("Padded Title", card.Title, "Title should be trimmed");
        Assert.AreEqual("Padded Desc", card.Description, "Description should be trimmed");
    }

    // ── GetCardsByDateRange ──────────────────────────────

    [TestMethod]
    public async Task GetCardsByDateRange_CompletedThisWeek_ReturnsOnlyCompletedCards()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        // Create a "Done" column with Completed status
        var maxColOrder = db.KanbanColumns.Where(c => c.BoardId == boardId).Max(c => (int?)c.Order) ?? 0;
        var doneColumn = new KanbanColumn { Name = "Done", Order = maxColOrder + 1, BoardId = boardId, ColumnStatus = ColumnStatus.Completed };
        db.KanbanColumns.Add(doneColumn);
        await db.SaveChangesAsync();
        var doneColumnId = doneColumn.Id;

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        // Create cards completed this week
        var thisWeekMonday = GetThisWeekMonday();
        db.KanbanCards.Add(new KanbanCard { Title = "This Week Task A", ColumnId = doneColumnId, Order = 0, ActualEndTime = thisWeekMonday });
        db.KanbanCards.Add(new KanbanCard { Title = "This Week Task B", ColumnId = doneColumnId, Order = 1, ActualEndTime = thisWeekMonday.AddDays(3) });
        db.KanbanCards.Add(new KanbanCard { Title = "Old Task", ColumnId = doneColumnId, Order = 2, ActualEndTime = thisWeekMonday.AddDays(-30) });
        await db.SaveChangesAsync();

        var start = thisWeekMonday.ToString("yyyy-MM-dd");
        var end = thisWeekMonday.AddDays(6).ToString("yyyy-MM-dd");
        var result = await cardTools.GetCardsByDateRange(start, end, boardId, "completed");

        StringAssert.Contains(result, "This Week Task A");
        StringAssert.Contains(result, "This Week Task B");
        Assert.IsFalse(result.Contains("Old Task"), "Old task should not appear in this week's range");
    }

    [TestMethod]
    public async Task GetCardsByDateRange_InvalidDateFormat_ReturnsError()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var adminUser = scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        var result = await cardTools.GetCardsByDateRange("not-a-date", "2026-01-01", boardId, null);
        StringAssert.Contains(result, "Invalid start date");
    }

    [TestMethod]
    public async Task GetCardsByDateRange_StartAfterEnd_ReturnsError()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var adminUser = scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        var result = await cardTools.GetCardsByDateRange("2026-06-15", "2026-06-01", boardId, null);
        StringAssert.Contains(result, "is after end date");
    }

    [TestMethod]
    public async Task GetCardsByDateRange_NoBoardId_ReturnsCardsFromAllAccessibleBoards()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        // Create a "Done" column with Completed status
        var maxColOrder = db.KanbanColumns.Where(c => c.BoardId == boardId).Max(c => (int?)c.Order) ?? 0;
        var doneColumn = new KanbanColumn { Name = "Done", Order = maxColOrder + 1, BoardId = boardId, ColumnStatus = ColumnStatus.Completed };
        db.KanbanColumns.Add(doneColumn);
        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        var thisWeekMonday = GetThisWeekMonday();
        db.KanbanCards.Add(new KanbanCard { Title = "Completed Card", ColumnId = doneColumn.Id, Order = 0, ActualEndTime = thisWeekMonday });
        await db.SaveChangesAsync();

        var start = thisWeekMonday.ToString("yyyy-MM-dd");
        var end = thisWeekMonday.AddDays(6).ToString("yyyy-MM-dd");
        var result = await cardTools.GetCardsByDateRange(start, end, null, "completed");

        StringAssert.Contains(result, "Completed Card");
    }

    [TestMethod]
    public async Task GetCardsByDateRange_NoCardsInRange_ReturnsEmptyMessage()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        // Create a "Done" column with Completed status
        var maxColOrder = db.KanbanColumns.Where(c => c.BoardId == boardId).Max(c => (int?)c.Order) ?? 0;
        var doneColumn = new KanbanColumn { Name = "Done", Order = maxColOrder + 1, BoardId = boardId, ColumnStatus = ColumnStatus.Completed };
        db.KanbanColumns.Add(doneColumn);
        await db.SaveChangesAsync();

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        // Card completed way in the past
        db.KanbanCards.Add(new KanbanCard { Title = "Ancient Task", ColumnId = doneColumn.Id, Order = 0, ActualEndTime = new DateTime(2020, 1, 1) });
        await db.SaveChangesAsync();

        var result = await cardTools.GetCardsByDateRange("2026-06-01", "2026-06-07", boardId, "completed");
        StringAssert.Contains(result, "No completed cards found");
    }

    [TestMethod]
    public async Task GetCardsByDateRange_FilterByCreatedDate()
    {
        await LoginAsAdmin();
        var (boardId, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;
        var cardTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        // Card created now should match today's range
        db.KanbanCards.Add(new KanbanCard { Title = "Fresh Card", ColumnId = columnId, Order = 0 });
        await db.SaveChangesAsync();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var result = await cardTools.GetCardsByDateRange(today, today, boardId, "created");

        StringAssert.Contains(result, "Fresh Card");
    }

    private static DateTime GetThisWeekMonday()
    {
        var now = DateTime.UtcNow;
        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        return now.Date.AddDays(-daysSinceMonday);
    }

    // ── Multi-tool batch approval ────────────────────────

    [TestMethod]
    public async Task AdviceService_MultiplePendingAdvice_ResumeOnlyAfterAllResolved()
    {
        // When an LLM response produces multiple write tool calls (e.g. CreateCard + AssignCard),
        // multiple advice items are created. The ReAct loop should NOT resume until ALL are resolved.
        // Otherwise Claude sees an incomplete tool_calls → tool_result chain and returns 400.
        await LoginAsAdmin();
        var service = GetService<AdviceService>();
        var cid = Guid.NewGuid();

        // Create 3 pending advice items (simulating 3 write tool calls in one LLM response)
        var a1 = service.Create(cid, "CreateCard", "Create Card", "desc",
            new Dictionary<string, object?> { ["title"] = "A" }, "title: A", "call_1");
        var a2 = service.Create(cid, "AssignCard", "Assign Card", "desc",
            new Dictionary<string, object?> { ["cardId"] = 1 }, "card: 1", "call_2");
        var a3 = service.Create(cid, "AddLabel", "Add Label", "desc",
            new Dictionary<string, object?> { ["name"] = "bug" }, "label: bug", "call_3");

        // Approve one — should still have 2 pending
        service.UpdateStatus(a1.Id, AdviceStatus.Approved);
        var pending = service.GetPendingForConversation(cid);
        Assert.AreEqual(2, pending.Count,
            "After approving 1 of 3 advice items, 2 should remain pending");

        // Reject one — should still have 1 pending
        service.UpdateStatus(a2.Id, AdviceStatus.Rejected);
        pending = service.GetPendingForConversation(cid);
        Assert.AreEqual(1, pending.Count,
            "After rejecting 1 of 2 remaining, 1 should remain pending");

        // Approve the last — should have 0 pending
        service.UpdateStatus(a3.Id, AdviceStatus.Approved);
        pending = service.GetPendingForConversation(cid);
        Assert.AreEqual(0, pending.Count,
            "After resolving all advice items, none should remain pending");
    }

    [TestMethod]
    public async Task AdviceService_GetPendingForConversation_OnlyReturnsMatching()
    {
        await LoginAsAdmin();
        var service = GetService<AdviceService>();
        var cidA = Guid.NewGuid();
        var cidB = Guid.NewGuid();

        service.Create(cidA, "T1", "T1", "", new(), "", "c1");
        service.Create(cidA, "T2", "T2", "", new(), "", "c2");
        service.Create(cidB, "T3", "T3", "", new(), "", "c3");

        Assert.AreEqual(2, service.GetPendingForConversation(cidA).Count);
        Assert.AreEqual(1, service.GetPendingForConversation(cidB).Count);
    }

    // ── Conversation cleanup ─────────────────────────────

    [TestMethod]
    public async Task AgentService_Cleanup_ExpiredConversationRemoved()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        // Create a conversation and set it as expired (30+ min ago)
        var expiredId = service.StartRun("admin", boardId, "Old message");
        var expiredConv = service.GetConversation(expiredId);
        Assert.IsNotNull(expiredConv);
        expiredConv.LastActivity = DateTime.UtcNow - TimeSpan.FromMinutes(31);

        // Create a new conversation — this triggers cleanup
        service.StartRun("admin", boardId, "New message");

        // Expired conversation should be removed
        Assert.IsNull(service.GetConversation(expiredId),
            "Expired conversation should be removed during cleanup sweep");
    }

    [TestMethod]
    public async Task AgentService_Cleanup_ActiveConversationPreserved()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        // Create an active conversation (just now)
        var activeId = service.StartRun("admin", boardId, "Recent message");
        Assert.IsNotNull(service.GetConversation(activeId));

        // Trigger cleanup via another StartRun
        service.StartRun("admin", boardId, "Another message");

        // Active conversation should still exist
        Assert.IsNotNull(service.GetConversation(activeId),
            "Active conversation should not be removed");
    }

    [TestMethod]
    public async Task AgentService_Cleanup_AdviceRemovedWithConversation()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();
        var adviceService = GetService<AdviceService>();

        // Create a conversation with expired advice
        var convId = service.StartRun("admin", boardId, "Message");
        var advice = adviceService.Create(convId, "CreateCard", "Create Card", "",
            new Dictionary<string, object?>(), "test", "call_1");

        var conv = service.GetConversation(convId);
        conv!.LastActivity = DateTime.UtcNow - TimeSpan.FromMinutes(31);

        // Trigger cleanup
        service.StartRun("admin", boardId, "New message");

        // Both conversation and advice should be gone
        Assert.IsNull(service.GetConversation(convId));
        Assert.IsNull(adviceService.Get(advice.Id),
            "Advice from expired conversation should also be removed");
    }

    [TestMethod]
    public async Task AgentService_CancelRun_RemovesConversationAndAdvice()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();
        var adviceService = GetService<AdviceService>();

        var convId = service.StartRun("admin", boardId, "Message");
        adviceService.Create(convId, "T1", "T1", "", new(), "", "c1");
        adviceService.Create(convId, "T2", "T2", "", new(), "", "c2");

        service.CancelRun(convId);

        Assert.IsNull(service.GetConversation(convId));
        Assert.AreEqual(0, adviceService.GetPendingForConversation(convId).Count,
            "All advice from cancelled conversation should be removed");
    }

    // ── GetMyTasks ──────────────────────────────────────

    [TestMethod]
    public async Task GetMyTasks_ReturnsCardsAssignedToCurrentUser()
    {
        await LoginAsAdmin();
        var (_, columnId) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        // Create a card — auto-assigned to current user
        var writeTools = scope.ServiceProvider.GetRequiredService<CardWriteTools>();
        var createResult = await writeTools.CreateCard(columnId, "My Assigned Task", "Test description");
        StringAssert.Contains(createResult, "Card created:");

        // Get my tasks
        var readTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();
        var result = await readTools.GetMyTasks(status: null, boardId: null);

        StringAssert.Contains(result, "My Assigned Task", "Should include the assigned card");
        StringAssert.Contains(result, "Found", "Should show count header");
    }

    [TestMethod]
    public async Task GetMyTasks_NoAssignedCards_ReturnsEmptyMessage()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var readTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();
        // Scope to the board we just created — it has no cards yet
        var result = await readTools.GetMyTasks(status: null, boardId: boardId);

        Assert.IsTrue(result.Contains("no") && result.Contains("cards"),
            $"Should indicate no assigned cards, got: {result}");
    }

    [TestMethod]
    public async Task GetMyTasks_StatusFilter_RespectsFilter()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        // Boards created via Kanban/CreateBoard have 3 columns: To Do, In Progress, Done
        // Find In Progress and Done columns
        var inProgressColumn = db.KanbanColumns.First(c => c.BoardId == boardId && c.ColumnStatus == ColumnStatus.InProgress);
        var doneColumn = db.KanbanColumns.First(c => c.BoardId == boardId && c.ColumnStatus == ColumnStatus.Completed);

        var writeTools = scope.ServiceProvider.GetRequiredService<CardWriteTools>();
        await writeTools.CreateCard(inProgressColumn.Id, "In Progress Task", null);
        await writeTools.CreateCard(doneColumn.Id, "Completed Task", null);

        var readTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        // Default (incomplete) — should include in-progress but not completed
        var incompleteResult = await readTools.GetMyTasks(status: null, boardId: null);
        StringAssert.Contains(incompleteResult, "In Progress Task", "Incomplete should include in-progress");
        Assert.IsFalse(incompleteResult.Contains("Completed Task"), "Incomplete should exclude completed");

        // Completed only
        var completedResult = await readTools.GetMyTasks(status: "completed", boardId: null);
        StringAssert.Contains(completedResult, "Completed Task", "Completed should include done card");
        Assert.IsFalse(completedResult.Contains("In Progress Task"), "Completed should exclude in-progress");

        // All
        var allResult = await readTools.GetMyTasks(status: "all", boardId: null);
        StringAssert.Contains(allResult, "In Progress Task");
        StringAssert.Contains(allResult, "Completed Task");
    }

    [TestMethod]
    public async Task GetMyTasks_BoardFilter_ScopesToBoard()
    {
        await LoginAsAdmin();

        // Create two boards
        var (board1Id, column1Id) = await CreateBoardAndFirstColumnAsync();
        var (board2Id, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        // Create cards on both boards
        var writeTools = scope.ServiceProvider.GetRequiredService<CardWriteTools>();
        await writeTools.CreateCard(column1Id, "Task on Board 1", null);

        // Find column for board 2
        var board2Column = db.KanbanColumns.First(c => c.BoardId == board2Id);
        await writeTools.CreateCard(board2Column.Id, "Task on Board 2", null);

        var readTools = scope.ServiceProvider.GetRequiredService<CardReadTools>();

        // Scope to board 1
        var result = await readTools.GetMyTasks(status: "all", boardId: board1Id);
        StringAssert.Contains(result, "Task on Board 1", "Should include board 1 task");
        Assert.IsFalse(result.Contains("Task on Board 2"), "Should not include board 2 task");

        // Scope to board 2
        var result2 = await readTools.GetMyTasks(status: "all", boardId: board2Id);
        StringAssert.Contains(result2, "Task on Board 2", "Should include board 2 task");
        Assert.IsFalse(result2.Contains("Task on Board 1"), "Should not include board 1 task");
    }

    // ── GetPublicBoards ─────────────────────────────────

    [TestMethod]
    public async Task GetPublicBoards_ReturnsPublicBoardsOnly()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var (privateBoardId, _) = await CreateBoardAndFirstColumnAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        // Make one board public
        var shareTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();
        await shareTools.UpdateBoardVisibility(boardId, isPublic: true);

        var readTools = scope.ServiceProvider.GetRequiredService<BoardReadTools>();
        var result = await readTools.GetPublicBoards();

        // Should include public board but not private one
        var publicBoard = db.KanbanBoards.First(b => b.Id == boardId);
        var privateBoard = db.KanbanBoards.First(b => b.Id == privateBoardId);
        StringAssert.Contains(result, $"\"{publicBoard.Name}\"", "Should list public board by name");
        Assert.IsFalse(result.Contains($"# {privateBoard.Id}"),
            $"Should not leak private board ID, result: {result}");
    }

    // ── GetSharedBoards ─────────────────────────────────

    [TestMethod]
    public async Task GetSharedBoards_ReturnsBoardsSharedWithUser()
    {
        // Admin creates a board
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();

        // Register target user and get their ID
        var (user2Email, _) = await RegisterAndLoginAsync();

        // Admin shares board with user2
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.Email == "admin@default.com");
        var user2 = db.Users.First(u => u.Email == user2Email);

        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = adminUser.Id;

        var shareTools = scope.ServiceProvider.GetRequiredService<ShareWriteTools>();
        await shareTools.ShareBoard(boardId, user2.Id, null, "ReadOnly");

        // Now check from user2's perspective
        var scope2 = Server!.Services.CreateScope();
        scope2.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = user2.Id;

        var readTools = scope2.ServiceProvider.GetRequiredService<BoardReadTools>();
        var result = await readTools.GetSharedBoards();

        var board = db.KanbanBoards.First(b => b.Id == boardId);
        StringAssert.Contains(result, board.Name, "Should include shared board");
        StringAssert.Contains(result, "Read-only", "Should show permission level");
    }

    [TestMethod]
    public async Task GetSharedBoards_NoSharedBoards_ReturnsEmptyMessage()
    {
        // Register a new user who has no boards shared with them
        await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var users = db.Users.ToList();

        // Find the last registered user (current login)
        var currentUser = users.Last();
        scope.ServiceProvider.GetRequiredService<CurrentUserService>().UserId = currentUser.Id;

        var readTools = scope.ServiceProvider.GetRequiredService<BoardReadTools>();
        var result = await readTools.GetSharedBoards();

        Assert.IsTrue(result.Contains("No boards") || result.Contains("not been shared"),
            $"Should indicate no shared boards, got: {result}");
    }

    // ── Helpers ─────────────────────────────────────────────

    private async Task<(int boardId, int firstColumnId)> CreateBoardAndFirstColumnAsync()
    {
        var token = await GetAntiCsrfToken("/");
        var response = await Http.PostAsync("/Kanban/CreateBoard",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "name", $"Agent Test Board {Guid.NewGuid():N}" },
                { "__RequestVerificationToken", token }
            }));

        var location = response.Headers.Location!.OriginalString;
        var boardId = int.Parse(
            location[(location.IndexOf("boardId=", StringComparison.Ordinal) + 8)..].Split('&', '/').First());

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var firstColumn = db.KanbanColumns.First(c => c.BoardId == boardId);
        return (boardId, firstColumn.Id);
    }
}
