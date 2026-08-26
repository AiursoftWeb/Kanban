using Aiursoft.AgentExam.Core.Loading;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.ExamRunner.Configuration;
using Aiursoft.Kanban.Services.Agent;

namespace Aiursoft.Kanban.ExamRunner.Tests;

[TestClass]
[DoNotParallelize]
public class BaselineScenarioTests
{
    private const string ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    [TestMethod]
    public async Task BaselineScenarios_LoadWithProductionToolsAndCoverEveryDimension()
    {
        var fixtureDirectory = GetFixtureDirectory();
        var knownTools = ToolRegistry.GetRegisteredToolNames()
            .ToHashSet(StringComparer.Ordinal);

        var scenarios = await new ScenarioLoader(knownTools).LoadAsync(
            Path.Combine(fixtureDirectory, "Scenarios", "*.json"));

        CollectionAssert.AreEquivalent(
            new[]
            {
                "read-urgent-cards",
                "write-card-lifecycle",
                "safety-private-board",
                "ambiguity-duplicate-card-title"
            },
            scenarios.Select(scenario => scenario.Id).ToArray());
        Assert.IsTrue(scenarios.All(scenario => scenario.Steps.Length > 0));

        var positiveDimensions = scenarios
            .SelectMany(scenario => scenario.Steps)
            .SelectMany(step => step.Expect.Trace
                .Concat(step.Expect.State)
                .Concat(step.Expect.Response))
            .Where(assertion => assertion.Points > 0)
            .Select(assertion => assertion.Dimension)
            .Distinct()
            .ToArray();
        CollectionAssert.AreEquivalent(
            Enum.GetValues<EvaluationDimension>(),
            positiveDimensions);
    }

    [TestMethod]
    public async Task ExampleConfiguration_LoadsCommittedBaselineWithoutEndpointRequest()
    {
        var previousCredential = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(ApiKeyEnvironmentVariable, "fixture-only-credential");
        try
        {
            var fixtureDirectory = GetFixtureDirectory();
            var loaded = await ExamConfigurationLoader.LoadAsync(
                Path.Combine(fixtureDirectory, "exam-config.example.json"));

            Assert.AreEqual("1.0", loaded.Configuration.SchemaVersion);
            Assert.AreEqual(
                Path.Combine(fixtureDirectory, "Scenarios", "**", "*.json"),
                loaded.ScenarioPatterns.Single());
            Assert.AreEqual(Path.Combine(fixtureDirectory, "reports"), loaded.OutputDirectory);

            var candidate = loaded.Candidates.Single();
            Assert.AreEqual("claude-opus", candidate.Candidate.Id);
            Assert.AreEqual("https://gateway.example.test/v1/messages", candidate.Candidate.Endpoint);
            Assert.AreEqual("claude-opus-5", candidate.Candidate.Model);
            Assert.AreEqual("production", candidate.Candidate.StrategyId);
            Assert.AreEqual(1, candidate.Candidate.Repetitions);
            Assert.AreEqual("apiKey", candidate.Authentication.Mode);
            Assert.AreEqual(ApiKeyEnvironmentVariable, candidate.Authentication.EnvironmentVariable);
            Assert.AreEqual("fixture-only-credential", candidate.Credential);
            var tools = candidate.Candidate.Tools;
            Assert.IsNotNull(tools);
            Assert.IsTrue(tools.Length > 0);

            var registeredTools = ToolRegistry.GetRegisteredToolNames()
                .ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(tools.All(registeredTools.Contains));

            var scenarios = await new ScenarioLoader(registeredTools).LoadAsync(
                loaded.ScenarioPatterns);
            Assert.HasCount(4, scenarios);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ApiKeyEnvironmentVariable, previousCredential);
        }
    }

    private static string GetFixtureDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "ExamFixtures");
}
