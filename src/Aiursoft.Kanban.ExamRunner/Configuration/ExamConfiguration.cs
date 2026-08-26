using Aiursoft.AgentExam.Core.Models;

namespace Aiursoft.Kanban.ExamRunner.Configuration;

public sealed record ExamConfiguration
{
    public string SchemaVersion { get; init; } = "1.0";
    public required string[] Scenarios { get; init; }
    public string OutputDirectory { get; init; } = "reports";
    public double FailBelow { get; init; } = 70;
    public required CandidateConfiguration[] Candidates { get; init; }
}

public sealed record CandidateConfiguration
{
    public required string Id { get; init; }
    public required string Endpoint { get; init; }
    public required string Model { get; init; }
    public string StrategyId { get; init; } = "production";
    public string? Prompt { get; init; }
    public string? PromptFile { get; init; }
    public required string[] Tools { get; init; }
    public CandidateAuthentication Authentication { get; init; } = new();
    public int Repetitions { get; init; } = 1;

    public ExamCandidate ToExamCandidate(string? resolvedPrompt) => new()
    {
        Id = Id,
        Endpoint = Endpoint,
        Model = Model,
        StrategyId = StrategyId,
        Prompt = resolvedPrompt,
        Tools = Tools,
        Repetitions = Repetitions
    };
}

public sealed record CandidateAuthentication
{
    public string Mode { get; init; } = "none";
    public string? EnvironmentVariable { get; init; }
}

public sealed record LoadedExamConfiguration(
    ExamConfiguration Configuration,
    string ConfigurationDirectory,
    IReadOnlyList<string> ScenarioPatterns,
    string OutputDirectory,
    IReadOnlyList<LoadedCandidate> Candidates);

public sealed record LoadedCandidate(
    ExamCandidate Candidate,
    CandidateAuthentication Authentication,
    string? Credential,
    string? SystemPrompt);
