using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Evaluation;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.ExamRunner.Configuration;
using Aiursoft.Kanban.ExamRunner.Execution;
using Aiursoft.Kanban.ExamRunner.Transport;
using Aiursoft.Kanban.Services.Agent;

namespace Aiursoft.Kanban.ExamRunner.Tests;

[TestClass]
public class ExamConfigurationTests
{
    [TestMethod]
    public async Task LoadAsync_ResolvesContainedPathsAndExplicitAuthentication()
    {
        var directory = CreateTempDirectory();
        const string environmentVariable = "KANBAN_EXAM_TEST_KEY";
        Environment.SetEnvironmentVariable(environmentVariable, "secret-value");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "prompt.txt"), "Candidate prompt");
            var configPath = Path.Combine(directory, "exam-config.json");
            await File.WriteAllTextAsync(configPath, $$"""
            {
              "scenarios": ["scenarios/*.json"],
              "outputDirectory": "reports",
              "candidates": [{
                "id": "candidate-one",
                "endpoint": "http://127.0.0.1/v1/messages",
                "model": "test-model",
                "promptFile": "prompt.txt",
                "tools": ["GetBoards"],
                "authentication": {
                  "mode": "apiKey",
                  "environmentVariable": "{{environmentVariable}}"
                }
              }]
            }
            """);

            var loaded = await ExamConfigurationLoader.LoadAsync(configPath);

            Assert.AreEqual(Path.Combine(directory, "scenarios", "*.json"), loaded.ScenarioPatterns.Single());
            Assert.AreEqual(Path.Combine(directory, "reports"), loaded.OutputDirectory);
            Assert.AreEqual("Candidate prompt", loaded.Candidates.Single().SystemPrompt);
            Assert.AreEqual("secret-value", loaded.Candidates.Single().Credential);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_RejectsEscapesAndEmptyWhitelists()
    {
        var directory = CreateTempDirectory();
        try
        {
            var escapePath = Path.Combine(directory, "escape.json");
            await File.WriteAllTextAsync(escapePath, """
            {
              "scenarios": ["../outside.json"],
              "candidates": [{
                "id": "candidate-one",
                "endpoint": "http://127.0.0.1/v1/messages",
                "model": "test-model",
                "tools": ["GetBoards"]
              }]
            }
            """);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ExamConfigurationLoader.LoadAsync(escapePath));

            var whitelistPath = Path.Combine(directory, "whitelist.json");
            await File.WriteAllTextAsync(whitelistPath, """
            {
              "scenarios": ["scenario.json"],
              "candidates": [{
                "id": "candidate-one",
                "endpoint": "http://127.0.0.1/v1/messages",
                "model": "test-model",
                "tools": []
              }]
            }
            """);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                ExamConfigurationLoader.LoadAsync(whitelistPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}

[TestClass]
public class CandidateModelClientTests
{
    [TestMethod]
    [DataRow("none")]
    [DataRow("apiKey")]
    [DataRow("bearer")]
    public async Task SendAsync_SendsOnlyConfiguredAuthenticationHeader(string mode)
    {
        using var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new CandidateModelClient(
            "http://127.0.0.1/v1/messages",
            "test-model",
            new CandidateAuthentication { Mode = mode },
            mode == "none" ? null : "top-secret",
            httpClient);

        await client.SendAsync("prompt", [new ClaudeMessage { Role = "user", Content = "hello" }], null);

        Assert.AreEqual(mode == "apiKey", handler.ApiKey != null);
        Assert.AreEqual(mode == "bearer", handler.Authorization != null);
        if (mode == "apiKey")
        {
            Assert.AreEqual("top-secret", handler.ApiKey);
            Assert.IsNull(handler.Authorization);
        }
        if (mode == "bearer")
        {
            Assert.AreEqual("Bearer top-secret", handler.Authorization);
            Assert.IsNull(handler.ApiKey);
        }
    }

    [TestMethod]
    public async Task SendAsync_DoesNotExposeResponseBodyOnFailure()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "top-secret");
        using var httpClient = new HttpClient(handler);
        using var client = new CandidateModelClient(
            "http://127.0.0.1/v1/messages",
            "test-model",
            new CandidateAuthentication { Mode = "bearer" },
            "top-secret",
            httpClient);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            client.SendAsync("prompt", [], null));

        Assert.IsFalse(exception.Message.Contains("top-secret", StringComparison.Ordinal));
    }

    private sealed class RecordingHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null) : HttpMessageHandler
    {
        public string? ApiKey { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ApiKey = request.Headers.TryGetValues("x-api-key", out var values)
                ? values.Single()
                : null;
            Authorization = request.Headers.Authorization?.ToString();
            var body = responseBody ?? """
            {"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}
            """;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

[TestClass]
public class ExamOrchestratorTests
{
    [TestMethod]
    public async Task RunAsync_UsesIsolatedProductionAttemptAndPrompt()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}");
        try
        {
            var scenario = Scenario("success") with
            {
                Setup = new ExamSetup
                {
                    Users = [new SetupUser { Id = "user.owner", DisplayName = "Owner" }],
                    Boards = [new SetupBoard { Id = "board.main", Name = "Main", OwnerId = "user.owner" }],
                    Columns = [new SetupColumn { Id = "column.main", BoardId = "board.main", Name = "To Do" }]
                }
            };
            var writer = new RecordingWriter();
            var candidate = new ExamCandidate
            {
                Id = "candidate-one",
                Endpoint = "http://127.0.0.1/v1/messages",
                Model = "test-model",
                Tools = ["GetBoards"]
            };
            var loaded = Loaded(output, candidate, "Exam-only prompt");
            var handler = new PromptRecordingHandler();

            var result = await new ExamOrchestrator(
                new FakeScenarioLoader([scenario]),
                new AssertionEvaluator(),
                writer,
                _ => new HttpClient(handler, false),
                new TestTimeProvider(DateTimeOffset.Parse("2026-08-26T11:52:30Z")))
                .RunAsync(loaded);

            var run = writer.Reports.Single().Candidates.Single();
            Assert.AreEqual(0, result.ExitCode);
            Assert.AreEqual(
                Path.Combine(output, "2026-08-26-115230"),
                result.OutputDirectory);
            Assert.AreEqual(
                Path.Combine(result.OutputDirectory, "candidate-one", "repetition-1"),
                writer.ReportDirectories.Single());
            Assert.AreEqual(result.OutputDirectory, writer.SummaryDirectory);
            Assert.AreEqual("Exam-only prompt", handler.SystemPrompt);
            Assert.IsFalse(run.Score.Incomplete);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task RunAsync_PreservesExistingTimestampDirectory()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}");
        var existing = Path.Combine(output, "2026-08-26-115230");
        try
        {
            Directory.CreateDirectory(existing);
            var sentinel = Path.Combine(existing, "sentinel.txt");
            await File.WriteAllTextAsync(sentinel, "historical report");
            var scenario = Scenario("success") with
            {
                Setup = new ExamSetup
                {
                    Users = [new SetupUser { Id = "user.owner", DisplayName = "Owner" }],
                    Boards = [new SetupBoard { Id = "board.main", Name = "Main", OwnerId = "user.owner" }],
                    Columns = [new SetupColumn { Id = "column.main", BoardId = "board.main", Name = "To Do" }]
                }
            };
            var writer = new RecordingWriter();
            var handler = new PromptRecordingHandler();
            var candidate = new ExamCandidate
            {
                Id = "candidate-one",
                Endpoint = "http://127.0.0.1/v1/messages",
                Model = "test-model",
                Tools = ["GetBoards"]
            };

            var result = await new ExamOrchestrator(
                new FakeScenarioLoader([scenario]),
                new AssertionEvaluator(),
                writer,
                _ => new HttpClient(handler, false),
                new TestTimeProvider(DateTimeOffset.Parse("2026-08-26T11:52:30Z")))
                .RunAsync(Loaded(output, candidate, null));

            Assert.AreEqual(
                Path.Combine(output, "2026-08-26-115230-01"),
                result.OutputDirectory);
            Assert.AreEqual("historical report", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task RunAsync_ConcurrentRunsReserveDistinctDirectories()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}");
        try
        {
            var scenario = Scenario("failure");
            var candidate = new ExamCandidate
            {
                Id = "candidate-one",
                Endpoint = "http://127.0.0.1/v1/messages",
                Model = "test-model",
                Tools = ["GetBoards"]
            };
            var loaded = Loaded(output, candidate, null);
            var timeProvider = new TestTimeProvider(
                DateTimeOffset.Parse("2026-08-26T11:52:30Z"));
            var first = new ExamOrchestrator(
                new FakeScenarioLoader([scenario]),
                new AssertionEvaluator(),
                new RecordingWriter(),
                _ => new HttpClient(new FailureHandler()),
                timeProvider);
            var second = new ExamOrchestrator(
                new FakeScenarioLoader([scenario]),
                new AssertionEvaluator(),
                new RecordingWriter(),
                _ => new HttpClient(new FailureHandler()),
                timeProvider);

            var results = await Task.WhenAll(
                first.RunAsync(loaded),
                second.RunAsync(loaded));

            Assert.AreNotEqual(results[0].OutputDirectory, results[1].OutputDirectory);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    Path.Combine(output, "2026-08-26-115230"),
                    Path.Combine(output, "2026-08-26-115230-01")
                },
                results.Select(result => result.OutputDirectory).ToArray());
            Assert.IsTrue(results.All(result => Directory.Exists(result.OutputDirectory)));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task RunAsync_ContinuesFailuresAndWritesEveryReport()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}");
        try
        {
            var scenarios = new[] { Scenario("first"), Scenario("second") };
            var loader = new FakeScenarioLoader(scenarios);
            var evaluator = new AssertionEvaluator();
            var writer = new RecordingWriter();
            var candidate = new ExamCandidate
            {
                Id = "candidate-one",
                Endpoint = "http://127.0.0.1/v1/messages",
                Model = "test-model",
                Tools = ["GetBoards"],
                Repetitions = 2
            };
            var loaded = Loaded(output, candidate, null);

            var result = await new ExamOrchestrator(
                loader,
                evaluator,
                writer,
                _ => new HttpClient(new FailureHandler()))
                .RunAsync(loaded);

            Assert.AreEqual(2, result.ExitCode);
            Assert.AreEqual(2, writer.Reports.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    Path.Combine(result.OutputDirectory, "candidate-one", "repetition-1"),
                    Path.Combine(result.OutputDirectory, "candidate-one", "repetition-2")
                },
                writer.ReportDirectories.ToArray());
            Assert.AreEqual(result.OutputDirectory, writer.SummaryDirectory);
            Assert.IsNotNull(writer.Summary);
            Assert.AreEqual(2, writer.Summary.Candidates.Single().IncompleteRuns);
            Assert.AreEqual(4, writer.Summary.Candidates.Single().InvalidScenarios);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    private static LoadedExamConfiguration Loaded(
        string output,
        ExamCandidate candidate,
        string? prompt) => new(
        new ExamConfiguration
        {
            Scenarios = ["scenario.json"],
            OutputDirectory = "reports",
            FailBelow = 0,
            Candidates = []
        },
        [Path.Combine(output, "scenario.json")],
        output,
        [new LoadedCandidate(candidate, new CandidateAuthentication(), null, prompt)]);

    private static ExamScenario Scenario(string id) => new()
    {
        SchemaVersion = "1.0",
        Id = id,
        Name = id,
        Domain = "kanban",
        FixedUtcNow = DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
        Setup = new ExamSetup(),
        Steps =
        [
            new ExamStep
            {
                UserId = "user.owner",
                BoardId = "board.main",
                UserMessage = "hello",
                Expect = new ExamExpectation
                {
                    Response =
                    [
                        new AssertionSpec
                        {
                            Id = "response",
                            Kind = "response",
                            Dimension = EvaluationDimension.IntentRecognition,
                            Points = 1,
                            Match = JsonSerializer.SerializeToElement("done")
                        }
                    ]
                }
            }
        ]
    };

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class PromptRecordingHandler : HttpMessageHandler
    {
        public string? SystemPrompt { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            SystemPrompt = document.RootElement.GetProperty("system").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class FakeScenarioLoader(IReadOnlyList<ExamScenario> scenarios) : IScenarioLoader
    {
        public Task<IReadOnlyList<ExamScenario>> LoadAsync(
            string path,
            CancellationToken cancellationToken = default) => Task.FromResult(scenarios);

        public Task<IReadOnlyList<ExamScenario>> LoadAsync(
            IEnumerable<string> patterns,
            CancellationToken cancellationToken = default) => Task.FromResult(scenarios);
    }

    private sealed class RecordingWriter : IReportWriter
    {
        public List<ExamReport> Reports { get; } = [];
        public List<string> ReportDirectories { get; } = [];
        public ExamSummaryReport? Summary { get; private set; }
        public string? SummaryDirectory { get; private set; }

        public Task WriteAsync(
            ExamReport report,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            ReportDirectories.Add(outputDirectory);
            return Task.CompletedTask;
        }

        public Task WriteSummaryAsync(
            ExamSummaryReport report,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            Summary = report;
            SummaryDirectory = outputDirectory;
            return Task.CompletedTask;
        }
    }
}
