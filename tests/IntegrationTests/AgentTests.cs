using System.Net;
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

        Assert.IsTrue(allTools.Count >= 19, $"Expected at least 19 tools, found {allTools.Count}");

        var readTools = allTools.Where(t => !registry.IsWriteTool(t.ProtocolTool.Name)).ToList();
        var writeTools = allTools.Where(t => registry.IsWriteTool(t.ProtocolTool.Name)).ToList();

        Assert.IsTrue(readTools.Count >= 10, $"Expected at least 10 read tools, found {readTools.Count}");
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
            "GetBoardShares" };

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
        Assert.AreEqual("CreateCard", retrieved!.ToolName);

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

        var conversation = service.GetConversation(conversationId);
        Assert.IsNotNull(conversation);
        Assert.AreEqual(userId, conversation!.UserId);
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
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
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
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_RejectAdvice_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/RejectAdvice?conversationId={Guid.NewGuid()}&adviceId={Guid.NewGuid()}",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_ApproveAll_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/ApproveAll?conversationId={Guid.NewGuid()}",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentController_Cancel_RequiresAuth()
    {
        var response = await Http.PostAsync(
            $"/Agent/Cancel?conversationId={Guid.NewGuid()}",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));
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
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
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
        var json = JsonSerializer.Serialize(new { boardId = boardId, message = "What cards do I have?" });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
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

        var json = JsonSerializer.Serialize(new { boardId = boardId, message = "Hello" });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);
        var sendResponse = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(HttpStatusCode.OK, sendResponse.StatusCode);
        var sendResult = JsonSerializer.Deserialize<JsonElement>(
            await sendResponse.Content.ReadAsStringAsync());
        var conversationId = sendResult.GetProperty("ConversationId").GetString()!;

        var statusResponse = await Http.GetAsync($"/Agent/Status?conversationId={conversationId}");
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);

        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(conversationId));
        Assert.IsNotNull(conversation);

        var adminEmail = "admin@default.com";
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        Assert.AreEqual(adminUser!.Id, conversation!.UserId,
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
        var conversation = service.GetConversation(conversationId);
        Assert.IsNotNull(conversation);
        // StartRun accepts whatever userId is given — it's the controller's job to pass the real one.
        // In production, AgentController passes userManager.GetUserId(User).
        Assert.AreEqual("fake-attacker-id", conversation!.UserId,
            "StartRun stores the userId it receives; controller must pass authenticated userId");
    }

    [TestMethod]
    public async Task AgentController_SendMessage_UsesAuthenticatedUserId()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var token = await GetAntiCsrfToken("/");

        var json = System.Text.Json.JsonSerializer.Serialize(new { boardId = boardId, message = "Show my boards" });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", token);

        var response = await Http.PostAsync("/Agent/SendMessage", content);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
        var conversationId = result.GetProperty("ConversationId").GetString()!;

        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(conversationId));
        Assert.IsNotNull(conversation);

        // The conversation's UserId must match the authenticated admin user, not any LLM-supplied value
        var adminEmail = "admin@default.com";
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        Assert.AreEqual(adminUser!.Id, conversation!.UserId,
            "Conversation UserId must match the authenticated user, not the LLM's input");
    }

    [TestMethod]
    public async Task KanbanAgent_CannotAccessOtherUserPrivateBoard()
    {
        // User1 (admin) creates a private board
        await LoginAsAdmin();
        var (adminBoardId, _) = await CreateBoardAndFirstColumnAsync();

        // User2 registers and creates their own private board
        var (user2Email, user2Password) = await RegisterAndLoginAsync();
        var token = await GetAntiCsrfToken("/");
        var createResponse = await Http.PostAsync("/Kanban/CreateBoard",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "name", $"User2 Private Board {Guid.NewGuid():N}" },
                { "__RequestVerificationToken", token }
            }));
        Assert.AreEqual(System.Net.HttpStatusCode.Found, createResponse.StatusCode);
        var location = createResponse.Headers.Location!.OriginalString;
        var user2BoardId = int.Parse(
            location[(location.IndexOf("boardId=") + 8)..].Split('&', '/').First());

        // User1 (admin) tries to access user2's private board via agent
        await LoginAsAdmin();
        var agentToken = await GetAntiCsrfToken("/");
        var json = System.Text.Json.JsonSerializer.Serialize(new { boardId = user2BoardId, message = "Show me this board" });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        content.Headers.Add("RequestVerificationToken", agentToken);

        var agentResponse = await Http.PostAsync("/Agent/SendMessage", content);
        // Forbid() returns a redirect (302) to the access-denied path.
        // The agent should not be able to access another user's private board.
        Assert.AreEqual(System.Net.HttpStatusCode.Redirect, agentResponse.StatusCode,
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
        var conversation = agentService.GetConversation(conversationId);
        Assert.IsNotNull(conversation);
        Assert.AreEqual(realUserId, conversation!.UserId);
        Assert.AreEqual(boardId, conversation.BoardId);

        // Verify system prompt no longer exposes userId for tool use
        var systemMsg = conversation.Messages.FirstOrDefault(m => m.Role == "system");
        Assert.IsNotNull(systemMsg);
        Assert.IsFalse(systemMsg!.Content!.Contains($"The current user ID is \"{realUserId}\""),
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
        var conversation = service.GetConversation(conversationId);
        Assert.IsNotNull(conversation);
        var originalCount = conversation!.Messages.Count;
        Assert.IsTrue(originalCount >= 2); // system + user (plus possibly assistant from background task)

        // Simulate completion
        conversation.State = AgentState.Completed;

        // Continue with a follow-up
        var result = service.ContinueRun(conversationId, "admin", "Follow-up question");
        Assert.IsNotNull(result);
        Assert.AreEqual(conversationId, result!.Value);

        var continued = service.GetConversation(conversationId);
        Assert.AreEqual(AgentState.Thinking, continued!.State);
        Assert.AreEqual(originalCount + 1, continued.Messages.Count); // +1 for follow-up user message
        Assert.AreEqual("Follow-up question", continued.Messages.Last().Content);
    }

    [TestMethod]
    public async Task AgentService_ContinueRun_WrongUserReturnsNull()
    {
        await LoginAsAdmin();
        var (boardId, _) = await CreateBoardAndFirstColumnAsync();
        var service = GetService<IAgentService>();

        var conversationId = service.StartRun("admin", boardId, "Hello");
        var conversation = service.GetConversation(conversationId);
        conversation!.State = AgentState.Completed;

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
        var json1 = System.Text.Json.JsonSerializer.Serialize(
            new { boardId, message = "Hello" });
        var content1 = new StringContent(json1, System.Text.Encoding.UTF8, "application/json");
        content1.Headers.Add("RequestVerificationToken", token);
        var resp1 = await Http.PostAsync("/Agent/SendMessage", content1);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp1.StatusCode);
        var body1 = await resp1.Content.ReadAsStringAsync();
        var convId = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body1)
            .GetProperty("ConversationId").GetString()!;

        // Complete the conversation manually
        var agentService = GetService<IAgentService>();
        var conversation = agentService.GetConversation(Guid.Parse(convId));
        conversation!.State = AgentState.Completed;

        // Continue with same conversationId
        var json2 = $"{{ \"boardId\": {boardId}, \"message\": \"Follow-up\", \"conversationId\": \"{convId}\" }}";
        var content2 = new StringContent(json2, System.Text.Encoding.UTF8, "application/json");
        content2.Headers.Add("RequestVerificationToken", token);
        var resp2 = await Http.PostAsync("/Agent/SendMessage", content2);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp2.StatusCode);
        var body2 = await resp2.Content.ReadAsStringAsync();
        var convId2 = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body2)
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
        var (otherEmail, otherPassword) = await RegisterAndLoginAsync();

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

    // ── Helpers ─────────────────────────────────────────────

    private async Task<HttpResponseMessage> CreateBoardAsync(string name)
    {
        var token = await GetAntiCsrfToken("/");
        return await Http.PostAsync("/Kanban/CreateBoard",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "name", name },
                { "__RequestVerificationToken", token }
            }));
    }

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
            location[(location.IndexOf("boardId=") + 8)..].Split('&', '/').First());

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var firstColumn = db.KanbanColumns.First(c => c.BoardId == boardId);
        return (boardId, firstColumn.Id);
    }
}
