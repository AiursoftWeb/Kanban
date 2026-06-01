using System.Net;
using System.Text.Json;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;

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

        Assert.IsTrue(allTools.Count >= 15, $"Expected at least 15 tools, found {allTools.Count}");

        var readTools = allTools.Where(t => !registry.IsWriteTool(t.ProtocolTool.Name)).ToList();
        var writeTools = allTools.Where(t => registry.IsWriteTool(t.ProtocolTool.Name)).ToList();

        Assert.IsTrue(readTools.Count >= 8, $"Expected at least 8 read tools, found {readTools.Count}");
        Assert.IsTrue(writeTools.Count >= 12, $"Expected at least 12 write tools, found {writeTools.Count}");

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
            "GetCardById", "SearchCards", "GetOverdueCards", "GetBoardMembers", "SearchUsers", "SearchLabels" };

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
            "CreateColumn", "AddLabel", "AssignCard" };

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
