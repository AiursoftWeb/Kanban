using System.Text.Json;
using Aiursoft.AgentExam.Core.Evaluation;
using Aiursoft.AgentExam.Core.Loading;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.AgentExam.Core.Reporting;
using Aiursoft.AgentExam.Core.Validation;

namespace Aiursoft.AgentExam.Core.Tests;

[TestClass]
public class ScenarioLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_LoadsValidScenario()
    {
        var path = await WriteScenarioAsync(ValidScenarioJson());
        var scenarios = await new ScenarioLoader().LoadAsync(path);
        Assert.AreEqual("create-card", scenarios.Single().Id);
    }

    [TestMethod]
    public async Task LoadAsync_RejectsMissingSetupCollection()
    {
        var path = await WriteScenarioAsync(ValidScenarioJson().Replace("\"cards\": []", "\"missingCards\": []"));
        var exception = await Assert.ThrowsExactlyAsync<ScenarioValidationException>(
            () => new ScenarioLoader().LoadAsync(path));
        StringAssert.Contains(exception.Message, "'cards' is missing");
    }

    [TestMethod]
    public async Task LoadAsync_RejectsUnknownStepReference()
    {
        var path = await WriteScenarioAsync(ValidScenarioJson().Replace("\"userId\": \"user.owner\"", "\"userId\": \"user.unknown\""));
        var exception = await Assert.ThrowsExactlyAsync<ScenarioValidationException>(
            () => new ScenarioLoader().LoadAsync(path));
        StringAssert.Contains(exception.Message, "unknown user");
    }

    [TestMethod]
    public async Task LoadAsync_RejectsUnknownJsonMember()
    {
        var path = await WriteScenarioAsync(ValidScenarioJson().Replace("\"description\": \"\",", "\"description\": \"\", \"unexpected\": true,"));
        var exception = await Assert.ThrowsExactlyAsync<ScenarioValidationException>(
            () => new ScenarioLoader().LoadAsync(path));
        StringAssert.Contains(exception.Message, "could not be mapped");
    }

    private static async Task<string> WriteScenarioAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-exam-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    internal static string ValidScenarioJson() => """
        {
          "schemaVersion": "1.0",
          "id": "create-card",
          "name": "Create card",
          "description": "",
          "domain": "kanban",
          "tags": [],
          "weight": 1,
          "timeoutSeconds": 120,
          "fixedUtcNow": "2026-08-24T00:00:00Z",
          "setup": {
            "users": [{ "id": "user.owner", "displayName": "Owner" }],
            "boards": [{ "id": "board.main", "name": "Main", "ownerId": "user.owner", "isPublic": false }],
            "columns": [],
            "shares": [],
            "cards": []
          },
          "steps": [{
            "userId": "user.owner",
            "boardId": "board.main",
            "userMessage": "Create a card",
            "expect": {
              "trace": [{
                "id": "tool-used",
                "kind": "tool",
                "dimension": "ToolSelection",
                "points": 1,
                "penalty": 0,
                "required": true,
                "hardFail": false,
                "match": { "name": "CreateCard" },
                "comment": ""
              }],
              "state": [],
              "response": []
            }
          }]
        }
        """;
}

[TestClass]
public class JsonMatcherTests
{
    [TestMethod]
    public void Matches_DoesNotConflateStringAndNumber()
    {
        using var expected = JsonDocument.Parse("\"1\"");
        using var actual = JsonDocument.Parse("1");
        Assert.IsFalse(JsonMatcher.Matches(expected.RootElement, actual.RootElement));
    }

    [TestMethod]
    public void Matches_SupportsNestedSubsetObjects()
    {
        using var expected = JsonDocument.Parse("{\"card\":{\"title\":\"Test\"}}");
        using var actual = JsonDocument.Parse("{\"card\":{\"title\":\"Test\",\"id\":1},\"ok\":true}");
        Assert.IsTrue(JsonMatcher.Matches(expected.RootElement, actual.RootElement));
    }

    [TestMethod]
    public void Matches_ExactRequiresExactObject()
    {
        using var expected = JsonDocument.Parse("{\"$exact\":{\"title\":\"Test\"}}");
        using var actual = JsonDocument.Parse("{\"title\":\"Test\",\"id\":1}");
        Assert.IsFalse(JsonMatcher.Matches(expected.RootElement, actual.RootElement));
    }

    [TestMethod]
    public void Matches_ContainsSupportsArrayMembers()
    {
        using var expected = JsonDocument.Parse("{\"$contains\":{\"title\":\"Test\"}}");
        using var actual = JsonDocument.Parse("[{\"title\":\"Test\",\"id\":1}]");
        Assert.IsTrue(JsonMatcher.Matches(expected.RootElement, actual.RootElement));
    }
}

[TestClass]
public class EvaluatorTests
{
    [TestMethod]
    public void Evaluate_RejectsDuplicateEvidenceIndexes()
    {
        var scenario = CreateScenario(2);
        var evidence = new AttemptEvidence([
            Evidence(0, "user.owner", "board.main"),
            Evidence(0, "user.owner", "board.main")
        ]);
        var result = new AssertionEvaluator().Evaluate(scenario, evidence);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(result.Error, "exactly once");
    }

    [TestMethod]
    public void Evaluate_RejectsMismatchedStepIdentity()
    {
        var scenario = CreateScenario(1);
        var result = new AssertionEvaluator().Evaluate(
            scenario,
            new AttemptEvidence([Evidence(0, "user.other", "board.main")]));
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(result.Error, "must match");
    }

    [TestMethod]
    public void Calculate_AllowsDimensionsWithoutAssertions()
    {
        var scenario = CreateScenario(1);
        var result = new AssertionEvaluator().Evaluate(
            scenario,
            new AttemptEvidence([Evidence(0, "user.owner", "board.main")]));
        var score = ScoreCalculator.Calculate([result]);
        Assert.AreEqual(0, score.Total);
        Assert.AreEqual(5, score.Dimensions.Count);
    }

    private static ExamScenario CreateScenario(int stepCount) => new()
    {
        SchemaVersion = "1.0",
        Id = "scenario",
        Name = "Scenario",
        Domain = "kanban",
        FixedUtcNow = DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
        Setup = new ExamSetup(),
        Steps = Enumerable.Range(0, stepCount).Select(_ => new ExamStep
        {
            UserId = "user.owner",
            BoardId = "board.main",
            UserMessage = "Test",
            Expect = new ExamExpectation()
        }).ToArray()
    };

    private static StepEvidence Evidence(int index, string userId, string boardId) => new(
        index,
        userId,
        boardId,
        [],
        JsonSerializer.SerializeToElement(new { }),
        string.Empty,
        1,
        TimeSpan.Zero);
}

[TestClass]
public class ReportWriterTests
{
    [TestMethod]
    public async Task WriteAsync_WritesEncodedReportsWithoutSecrets()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-report-{Guid.NewGuid():N}");
        var report = new ExamReport(
            "1.0",
            DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
            "hash",
            [new CandidateReport(
                "candidate-one",
                "model<script>",
                "production",
                "prompt-hash",
                new CandidateScore(0, false, [], []))]);

        await new ReportWriter().WriteAsync(report, output);

        var json = await File.ReadAllTextAsync(Path.Combine(output, "report.json"));
        var html = await File.ReadAllTextAsync(Path.Combine(output, "report.html"));
        Assert.IsFalse(json.Contains("api-key-secret", StringComparison.Ordinal));
        StringAssert.Contains(html, "model&lt;script&gt;");
        Assert.IsFalse(html.Contains("model<script>", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WriteSummaryAsync_WritesEncodedAggregateReport()
    {
        var output = Path.Combine(Path.GetTempPath(), $"agent-exam-summary-{Guid.NewGuid():N}");
        try
        {
            var report = new ExamSummaryReport(
                "1.0",
                DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
                "hash",
                [new CandidateSummary(
                    "candidate-one",
                    "model<script>",
                    "production",
                    "prompt-hash",
                    2,
                    50,
                    0,
                    100,
                    50,
                    .5,
                    1,
                    1,
                    [])]);

            await new ReportWriter().WriteSummaryAsync(report, output);

            var json = await File.ReadAllTextAsync(Path.Combine(output, "summary.json"));
            var html = await File.ReadAllTextAsync(Path.Combine(output, "summary.html"));
            Assert.IsFalse(json.Contains("api-key-secret", StringComparison.Ordinal));
            StringAssert.Contains(html, "model&lt;script&gt;");
            Assert.IsFalse(html.Contains("model<script>", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public void ValidateCandidate_RejectsPathLikeId()
    {
        var candidate = new ExamCandidate
        {
            Id = "../candidate",
            Endpoint = "https://example.test",
            Model = "model"
        };
        Assert.ThrowsExactly<ArgumentException>(() => ExamValidation.ValidateCandidate(candidate));
    }

    [TestMethod]
    public void ResolveContainedPath_RejectsEscapingOutputDirectory()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ExamValidation.ResolveContainedPath("/tmp/reports", "..", "outside"));
    }
}
