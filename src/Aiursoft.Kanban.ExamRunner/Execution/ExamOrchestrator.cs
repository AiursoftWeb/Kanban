using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Evaluation;
using Aiursoft.AgentExam.Core.Loading;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.AgentExam.Core.Reporting;
using Aiursoft.AgentExam.Core.Validation;
using Aiursoft.Kanban.ExamRunner.Configuration;
using Aiursoft.Kanban.ExamRunner.Transport;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Agent.Exam;

namespace Aiursoft.Kanban.ExamRunner.Execution;

public sealed record ExamExecutionResult(
    int ExitCode,
    string OutputDirectory,
    ExamSummaryReport Summary);

public sealed class ExamOrchestrator(
    IScenarioLoader? scenarioLoader = null,
    IAssertionEvaluator? evaluator = null,
    IReportWriter? reportWriter = null,
    Func<LoadedCandidate, HttpClient?>? httpClientFactory = null)
{
    private readonly IScenarioLoader scenarioLoader = scenarioLoader ?? new ScenarioLoader(
        ToolRegistry.GetRegisteredToolNames().ToHashSet(StringComparer.Ordinal));
    private readonly IAssertionEvaluator evaluator = evaluator ?? new AssertionEvaluator();
    private readonly IReportWriter reportWriter = reportWriter ?? new ReportWriter();
    private readonly Func<LoadedCandidate, HttpClient?> httpClientFactory =
        httpClientFactory ?? (_ => null);

    public async Task<ExamExecutionResult> RunAsync(
        LoadedExamConfiguration loaded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var scenarios = await scenarioLoader.LoadAsync(
            loaded.ScenarioPatterns,
            cancellationToken);
        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("No exam scenarios were loaded.");
        }
        var scenarioHash = ComputeHash(scenarios);
        foreach (var scenario in scenarios)
        {
            if (scenario.Domain != "kanban")
            {
                throw new InvalidOperationException(
                    $"Scenario '{scenario.Id}' has unsupported domain '{scenario.Domain}'.");
            }
        }
        var startedAt = DateTimeOffset.UtcNow;
        var allRuns = new List<CandidateReport>();

        foreach (var loadedCandidate in loaded.Candidates)
        {
            var candidate = loadedCandidate.Candidate;
            if (candidate.StrategyId != "production")
            {
                throw new InvalidOperationException(
                    $"Strategy '{candidate.StrategyId}' is not registered. No fallback was applied.");
            }
            var promptHash = ComputeHash(loadedCandidate.SystemPrompt ?? string.Empty);
            for (var repetition = 1; repetition <= candidate.Repetitions; repetition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = new List<ScenarioResult>();
                foreach (var scenario in scenarios)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(await RunScenarioAsync(
                        scenario,
                        loadedCandidate,
                        cancellationToken));
                }

                var report = new CandidateReport(
                    candidate.Id,
                    candidate.Model,
                    candidate.StrategyId,
                    promptHash,
                    ScoreCalculator.Calculate(results),
                    repetition);
                allRuns.Add(report);
                var runDirectory = ExamValidation.ResolveContainedPath(
                    loaded.OutputDirectory,
                    candidate.Id,
                    $"repetition-{repetition}");
                await reportWriter.WriteAsync(
                    new ExamReport("1.0", startedAt, scenarioHash, [report]),
                    runDirectory,
                    cancellationToken);
            }
        }

        var summary = new ExamSummaryReport(
            "1.0",
            startedAt,
            scenarioHash,
            loaded.Candidates.Select(candidate => BuildSummary(candidate, allRuns)).ToArray());
        await reportWriter.WriteSummaryAsync(summary, loaded.OutputDirectory, cancellationToken);
        var failed = summary.Candidates.Any(candidate =>
            candidate.IncompleteRuns > 0 ||
            candidate.Mean < loaded.Configuration.FailBelow);
        return new ExamExecutionResult(failed ? 2 : 0, loaded.OutputDirectory, summary);
    }

    private async Task<ScenarioResult> RunScenarioAsync(
        ExamScenario scenario,
        LoadedCandidate loadedCandidate,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new CandidateModelClient(
                loadedCandidate.Candidate.Endpoint,
                loadedCandidate.Candidate.Model,
                loadedCandidate.Authentication,
                loadedCandidate.Credential,
                httpClientFactory(loadedCandidate));
            await using var attempt = KanbanExamAttemptHost.Create(new KanbanExamAttemptHostOptions
            {
                ModelClient = client,
                TimeProvider = new FixedTimeProvider(scenario.FixedUtcNow),
                EnabledToolNames = loadedCandidate.Candidate.Tools!
                    .ToHashSet(StringComparer.Ordinal),
                SystemPromptOverride = loadedCandidate.SystemPrompt
            });
            var evidence = await attempt.RunAsync(scenario, cancellationToken);
            return evaluator.Evaluate(scenario, evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return evaluator.Evaluate(
                scenario,
                new AttemptEvidence([], false, SanitizeError(exception)));
        }
    }

    private static CandidateSummary BuildSummary(
        LoadedCandidate loadedCandidate,
        IReadOnlyList<CandidateReport> allRuns)
    {
        var runs = allRuns
            .Where(run => run.Id == loadedCandidate.Candidate.Id)
            .OrderBy(run => run.Repetition)
            .ToArray();
        var totals = runs.Select(run => run.Score.Total).ToArray();
        var mean = totals.Average();
        var variance = totals.Average(total => Math.Pow(total - mean, 2));
        var dimensions = Enum.GetValues<EvaluationDimension>()
            .Select(dimension =>
            {
                var scores = runs.Select(run => run.Score.Dimensions.Single(item =>
                    item.Dimension == dimension)).ToArray();
                return new DimensionSummary(
                    dimension,
                    scores.Average(score => score.Score),
                    scores.Average(score => score.Contribution));
            })
            .ToArray();
        return new CandidateSummary(
            loadedCandidate.Candidate.Id,
            loadedCandidate.Candidate.Model,
            loadedCandidate.Candidate.StrategyId,
            ComputeHash(loadedCandidate.SystemPrompt ?? string.Empty),
            runs.Length,
            mean,
            totals.Min(),
            totals.Max(),
            Math.Sqrt(variance),
            runs.Count(run => !run.Score.Incomplete) / (double)runs.Length,
            runs.Count(run => run.Score.Incomplete),
            runs.Sum(run => run.Score.Scenarios.Count(scenario => !scenario.Valid)),
            dimensions);
    }

    private static string ComputeHash(IReadOnlyList<ExamScenario> scenarios) =>
        ComputeHash(JsonSerializer.Serialize(scenarios, JsonDefaults.Options));

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SanitizeError(Exception exception) => exception switch
    {
        OperationCanceledException => "Scenario execution timed out.",
        HttpRequestException httpException when
            httpException.Message.StartsWith(
                "Candidate model endpoint returned HTTP ",
                StringComparison.Ordinal) => httpException.Message,
        HttpRequestException => "Candidate model endpoint request failed.",
        _ => $"Scenario execution failed: {exception.GetType().Name}."
    };
}
