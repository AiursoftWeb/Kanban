using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aiursoft.AgentExam.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<EvaluationDimension>))]
public enum EvaluationDimension
{
    IntentRecognition,
    ToolSelection,
    ParameterAccuracy,
    Safety,
    Efficiency
}

public static class AssertionKinds
{
    public const string Tool = "tool";
    public const string ForbidTool = "forbidTool";
    public const string State = "state";
    public const string Response = "response";
    public const string MaxToolCalls = "maxToolCalls";
    public const string MaxLoops = "maxLoops";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Tool,
        ForbidTool,
        State,
        Response,
        MaxToolCalls,
        MaxLoops
    };
}

public sealed record ExamScenario
{
    public required string SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string Domain { get; init; }
    public string[] Tags { get; init; } = [];
    public double Weight { get; init; } = 1;
    public int TimeoutSeconds { get; init; } = 120;
    public required DateTimeOffset FixedUtcNow { get; init; }
    public required ExamSetup Setup { get; init; }
    public required ExamStep[] Steps { get; init; }
}

public sealed record ExamSetup
{
    public SetupUser[] Users { get; init; } = [];
    public SetupBoard[] Boards { get; init; } = [];
    public SetupColumn[] Columns { get; init; } = [];
    public SetupShare[] Shares { get; init; } = [];
    public SetupCard[] Cards { get; init; } = [];
}

public sealed record SetupUser
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
}

public sealed record SetupBoard
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string OwnerId { get; init; }
    public bool IsPublic { get; init; }
}

public sealed record SetupColumn
{
    public required string Id { get; init; }
    public required string BoardId { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "NotStarted";
    public int Order { get; init; }
}

public sealed record SetupShare
{
    public required string BoardId { get; init; }
    public string? UserId { get; init; }
    public string? RoleName { get; init; }
    public required string Permission { get; init; }
}

public sealed record SetupCard
{
    public required string Id { get; init; }
    public required string ColumnId { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string CreatorUserId { get; init; }
    public string? AssignedUserId { get; init; }
    public string Priority { get; init; } = "None";
    public DateTimeOffset? DueDate { get; init; }
    public int Order { get; init; }
}

public sealed record ExamStep
{
    public required string UserId { get; init; }
    public required string BoardId { get; init; }
    public required string UserMessage { get; init; }
    public required ExamExpectation Expect { get; init; }
}

public sealed record ExamExpectation
{
    public AssertionSpec[] Trace { get; init; } = [];
    public AssertionSpec[] State { get; init; } = [];
    public AssertionSpec[] Response { get; init; } = [];
}

public sealed record AssertionSpec
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required EvaluationDimension Dimension { get; init; }
    public double Points { get; init; }
    public double Penalty { get; init; }
    public bool Required { get; init; }
    public bool HardFail { get; init; }
    public required JsonElement Match { get; init; }
    public string Comment { get; init; } = string.Empty;
}

public sealed record ToolTrace(
    string Name,
    JsonElement Parameters,
    JsonElement Result,
    int Loop,
    TimeSpan Elapsed);

public sealed record StepEvidence(
    int StepIndex,
    string UserId,
    string BoardId,
    IReadOnlyList<ToolTrace> Tools,
    JsonElement State,
    string Response,
    int LoopCount,
    TimeSpan Elapsed,
    bool Valid = true,
    string? Error = null);

public sealed record AttemptEvidence(
    IReadOnlyList<StepEvidence> Steps,
    bool Valid = true,
    string? Error = null);

public sealed record AssertionResult(
    string Id,
    EvaluationDimension Dimension,
    bool Matched,
    double Earned,
    double Maximum,
    bool Required,
    bool HardFail,
    string Detail);

public sealed record ScenarioResult(
    string ScenarioId,
    double Weight,
    bool Passed,
    bool Valid,
    IReadOnlyList<AssertionResult> Assertions,
    string? Error = null,
    IReadOnlyList<StepEvidence>? Steps = null)
{
    public double Score
    {
        get
        {
            if (!Valid)
            {
                return 0;
            }

            var maximum = Assertions.Sum(assertion => assertion.Maximum);
            return maximum == 0
                ? 0
                : Math.Clamp(Assertions.Sum(assertion => assertion.Earned) / maximum * 100, 0, 100);
        }
    }
}

public sealed record DimensionScore(
    EvaluationDimension Dimension,
    double Score,
    double Weight,
    double Contribution);

public sealed record CandidateScore(
    double Total,
    bool Incomplete,
    IReadOnlyList<DimensionScore> Dimensions,
    IReadOnlyList<ScenarioResult> Scenarios);

public sealed record ExamCandidate
{
    public required string Id { get; init; }
    public required string Endpoint { get; init; }
    public required string Model { get; init; }
    public string StrategyId { get; init; } = "production";
    public string? Prompt { get; init; }
    public string? PromptFile { get; init; }
    public string[]? Tools { get; init; }
    public string? ApiKeyEnv { get; init; }
    [JsonIgnore]
    public string? ApiKey { get; init; }
    public int Repetitions { get; init; } = 1;
}

public sealed record ExamReport(
    string SchemaVersion,
    DateTimeOffset StartedAt,
    string ScenarioHash,
    IReadOnlyList<CandidateReport> Candidates);

public sealed record CandidateReport(
    string Id,
    string Model,
    string StrategyId,
    string PromptHash,
    CandidateScore Score,
    int Repetition = 1);
