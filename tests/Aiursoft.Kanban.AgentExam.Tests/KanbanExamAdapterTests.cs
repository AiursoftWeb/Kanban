using System.Text.Json;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.AgentExam;
using Aiursoft.WebTools.Abstractions;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.Kanban.AgentExam.Tests;

[TestClass]
[DoNotParallelize]
public class KanbanExamAdapterTests
{
    private const string GetBoardById = "GetBoardById";
    private const string CreateCard = "CreateCard";
    private const string UpdateCardDetails = "UpdateCardDetails";

    [TestMethod]
    public async Task RunAsync_UsesWhitelistProductionToolAndStructuredSnapshot()
    {
        var model = new ScriptedModelClient(
            ToolUseResponse("call-1", CreateCard, new()
            {
                ["columnId"] = 1,
                ["title"] = "Created by exam",
                ["description"] = null,
                ["assignedUserId"] = ""
            }),
            TextResponse("Created."));
        using var host = await CreateHostAsync(model, ToolSet(CreateCard));
        using var scope = host.Services.CreateScope();

        var evidence = await scope.ServiceProvider
            .GetRequiredService<KanbanExamAdapter>()
            .RunAsync(CreateScenario());

        Assert.IsTrue(evidence.Valid, evidence.Error);
        Assert.AreEqual(1, evidence.Steps.Count);
        Assert.AreEqual(CreateCard, evidence.Steps[0].Tools.Single().Name);
        Assert.AreEqual(1, model.Requests[0].Tools.Count);
        Assert.AreEqual(CreateCard, model.Requests[0].Tools.Single().Name);
        var cards = evidence.Steps[0].State.GetProperty("cards").EnumerateArray().ToArray();
        Assert.AreEqual(3, cards.Length);
        Assert.IsTrue(cards.Any(card => card.GetProperty("title").GetString() == "Created by exam"));
        Assert.AreEqual(2, evidence.Steps[0].State.GetProperty("boards").GetArrayLength());
        Assert.AreEqual(1, evidence.Steps[0].State.GetProperty("labels").GetArrayLength());
        Assert.AreEqual(1, evidence.Steps[0].State.GetProperty("cardLabels").GetArrayLength());
        Assert.AreEqual(1, evidence.Steps[0].State.GetProperty("comments").GetArrayLength());
        Assert.AreEqual(2, evidence.Steps[0].State.GetProperty("subscriptions").GetArrayLength());
    }

    [TestMethod]
    public async Task RunAsync_RoleShareUsesProductionAuthorization()
    {
        var model = new ScriptedModelClient(
            ToolUseResponse("call-1", CreateCard, new()
            {
                ["columnId"] = 1,
                ["title"] = "Role editor card",
                ["description"] = null,
                ["assignedUserId"] = ""
            }),
            TextResponse("Created."));
        using var host = await CreateHostAsync(model, ToolSet(CreateCard));
        using var scope = host.Services.CreateScope();
        var scenario = CreateScenario(stepUser: "user.editor");

        var evidence = await scope.ServiceProvider
            .GetRequiredService<KanbanExamAdapter>()
            .RunAsync(scenario);

        Assert.IsTrue(evidence.Valid, evidence.Error);
        Assert.IsTrue(evidence.Steps[0].State.GetProperty("cards")
            .EnumerateArray()
            .Any(card => card.GetProperty("title").GetString() == "Role editor card"));
    }

    [TestMethod]
    public async Task RunAsync_CrossTenantWriteLeavesStateUnchanged()
    {
        var model = new ScriptedModelClient(
            ToolUseResponse("call-1", UpdateCardDetails, new()
            {
                ["cardId"] = 2,
                ["title"] = "Unauthorized change",
                ["description"] = null,
                ["plannedStartTime"] = null,
                ["dueDate"] = null,
                ["priority"] = 4,
                ["assignedUserId"] = ""
            }),
            TextResponse("Done."));
        using var host = await CreateHostAsync(model, ToolSet(UpdateCardDetails));
        using var scope = host.Services.CreateScope();

        var evidence = await scope.ServiceProvider
            .GetRequiredService<KanbanExamAdapter>()
            .RunAsync(CreateScenario());

        Assert.IsTrue(evidence.Valid, evidence.Error);
        var otherCard = evidence.Steps[0].State.GetProperty("cards")
            .EnumerateArray()
            .Single(card => card.GetProperty("id").GetString() == "card.other");
        Assert.AreEqual("Other card", otherCard.GetProperty("title").GetString());
        StringAssert.Contains(evidence.Steps[0].Tools.Single().Result.GetString()!, "permission");
    }

    [TestMethod]
    public async Task RunAsync_InvalidParametersDoNotWrite()
    {
        var model = new ScriptedModelClient(
            ToolUseResponse("call-1", CreateCard, new()
            {
                ["columnId"] = 999,
                ["title"] = "Must not exist",
                ["description"] = null,
                ["assignedUserId"] = ""
            }),
            TextResponse("Done."));
        using var host = await CreateHostAsync(model, ToolSet(CreateCard));
        using var scope = host.Services.CreateScope();

        var evidence = await scope.ServiceProvider
            .GetRequiredService<KanbanExamAdapter>()
            .RunAsync(CreateScenario());

        Assert.IsTrue(evidence.Valid, evidence.Error);
        Assert.AreEqual(2, evidence.Steps[0].State.GetProperty("cards").GetArrayLength());
    }

    [TestMethod]
    public async Task Attempts_IsolateDatabaseClockAndWhitelist()
    {
        var firstModel = new ScriptedModelClient(TextResponse("First."));
        var secondModel = new ScriptedModelClient(TextResponse("Second."));
        using var firstHost = await CreateHostAsync(
            firstModel,
            ToolSet(CreateCard),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        using var secondHost = await CreateHostAsync(
            secondModel,
            ToolSet(GetBoardById),
            DateTimeOffset.Parse("2027-01-01T00:00:00Z"));
        using var firstScope = firstHost.Services.CreateScope();
        using var secondScope = secondHost.Services.CreateScope();

        var first = await firstScope.ServiceProvider.GetRequiredService<KanbanExamAdapter>()
            .RunAsync(CreateScenario());
        var second = await secondScope.ServiceProvider.GetRequiredService<KanbanExamAdapter>()
            .RunAsync(CreateScenario());

        Assert.IsTrue(first.Valid, first.Error);
        Assert.IsTrue(second.Valid, second.Error);
        Assert.AreEqual(CreateCard, firstModel.Requests.Single().Tools.Single().Name);
        Assert.AreEqual(GetBoardById, secondModel.Requests.Single().Tools.Single().Name);
        Assert.AreEqual(2, first.Steps[0].State.GetProperty("cards").GetArrayLength());
        Assert.AreEqual(2, second.Steps[0].State.GetProperty("cards").GetArrayLength());
        Assert.IsTrue(ContainsMessageText(firstModel.Requests.Single().Messages, "2026"));
        Assert.IsTrue(ContainsMessageText(secondModel.Requests.Single().Messages, "2027"));
    }

    [TestMethod]
    public void AddKanbanExamAttempt_RejectsEmptyAndUnknownWhitelists()
    {
        var services = new ServiceCollection();
        var model = new ScriptedModelClient(TextResponse("Done."));
        Assert.ThrowsExactly<ArgumentException>(() => services.AddKanbanExamAttempt(new()
        {
            ModelClient = model,
            TimeProvider = TimeProvider.System,
            EnabledToolNames = new HashSet<string>(StringComparer.Ordinal)
        }));
        Assert.ThrowsExactly<ArgumentException>(() => services.AddKanbanExamAttempt(new()
        {
            ModelClient = model,
            TimeProvider = TimeProvider.System,
            EnabledToolNames = new HashSet<string>(["UnknownTool"], StringComparer.Ordinal)
        }));
    }

    private static IReadOnlySet<string> ToolSet(params string[] tools) =>
        new HashSet<string>(tools, StringComparer.Ordinal);

    private static bool ContainsMessageText(
        IEnumerable<ClaudeMessage> messages,
        string text) => messages.Any(message =>
        message.Content.ToString()?.Contains(text, StringComparison.Ordinal) == true);

    private static async Task<IHost> CreateHostAsync(
        IAgentModelClient modelClient,
        IReadOnlySet<string> enabledTools,
        DateTimeOffset? utcNow = null)
    {
        var port = CSTools.Tools.Network.GetAvailablePort();
        var host = await AppAsync<Startup>(
            [],
            port,
            plugins: [new ExamPlugin(modelClient, enabledTools, new FixedTimeProvider(utcNow ?? DateTimeOffset.Parse("2026-08-24T00:00:00Z")))]);
        await host.UpdateDbAsync<TemplateDbContext>();
        await host.StartAsync();
        return host;
    }

    private static ExamScenario CreateScenario(string stepUser = "user.owner") => new()
    {
        SchemaVersion = "1.0",
        Id = "adapter-scenario",
        Name = "Adapter scenario",
        Domain = "kanban",
        FixedUtcNow = DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
        Setup = new ExamSetup
        {
            Users =
            [
                new SetupUser { Id = "user.owner", DisplayName = "Owner" },
                new SetupUser { Id = "user.editor", DisplayName = "Editor", Roles = ["Editors"] },
                new SetupUser { Id = "user.other", DisplayName = "Other" }
            ],
            Boards =
            [
                new SetupBoard { Id = "board.main", Name = "Main", OwnerId = "user.owner" },
                new SetupBoard { Id = "board.other", Name = "Other", OwnerId = "user.other" }
            ],
            Columns =
            [
                new SetupColumn { Id = "column.main", BoardId = "board.main", Name = "To Do" },
                new SetupColumn { Id = "column.other", BoardId = "board.other", Name = "To Do" }
            ],
            Shares =
            [
                new SetupShare { BoardId = "board.main", RoleName = "Editors", Permission = "Editable" }
            ],
            Cards =
            [
                new SetupCard { Id = "card.main", ColumnId = "column.main", Title = "Main card", CreatorUserId = "user.owner" },
                new SetupCard { Id = "card.other", ColumnId = "column.other", Title = "Other card", CreatorUserId = "user.other" }
            ],
            Labels =
            [
                new SetupLabel { Id = "label.important", Name = "Important", CardIds = ["card.main"] }
            ],
            Comments =
            [
                new SetupComment { Id = "comment.first", CardId = "card.main", AuthorUserId = "user.owner", Content = "Initial comment" }
            ],
            Subscriptions =
            [
                new SetupSubscription { CardId = "card.main", UserId = "user.owner" }
            ]
        },
        Steps =
        [
            new ExamStep
            {
                UserId = stepUser,
                BoardId = "board.main",
                UserMessage = "Perform the requested operation.",
                Expect = new ExamExpectation()
            }
        ]
    };

    private static ClaudeResponse TextResponse(string text) => new()
    {
        Content = [ClaudeContentBlock.TextBlock(text)],
        StopReason = "end_turn"
    };

    private static ClaudeResponse ToolUseResponse(
        string id,
        string name,
        Dictionary<string, object?> input) => new()
    {
        Content = [ClaudeContentBlock.ToolUse(id, name, input)],
        StopReason = "tool_use"
    };

    private sealed record ModelRequest(
        IReadOnlyList<ClaudeMessage> Messages,
        IReadOnlyList<ClaudeTool> Tools);

    private sealed class ScriptedModelClient(params ClaudeResponse[] responses) : IAgentModelClient
    {
        private readonly Queue<ClaudeResponse> _responses = new(responses);
        public List<ModelRequest> Requests { get; } = [];

        public Task<ClaudeResponse> SendAsync(
            string systemPrompt,
            List<ClaudeMessage> messages,
            List<ClaudeTool>? tools,
            CancellationToken cancellationToken = default,
            int maxTokens = 4096)
        {
            Requests.Add(new ModelRequest(messages, tools ?? []));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ExamPlugin(
        IAgentModelClient modelClient,
        IReadOnlySet<string> enabledTools,
        TimeProvider timeProvider) : IWebAppPlugin
    {
        public bool ShouldAddThisPlugin() => true;
        public Task PreServiceConfigure(WebApplicationBuilder builder) => Task.CompletedTask;

        public Task PostServiceConfigure(WebApplicationBuilder builder)
        {
            builder.Services.AddKanbanExamAttempt(new KanbanExamAttemptOptions
            {
                ModelClient = modelClient,
                TimeProvider = timeProvider,
                EnabledToolNames = enabledTools
            });
            return Task.CompletedTask;
        }

        public Task AppConfiguration(WebApplication builder) => Task.CompletedTask;
    }
}
