using System.Text.Json;
using System.Text.Json.Serialization;
using Aiursoft.AgentExam.Core.Models;

namespace Aiursoft.AgentExam.Core.Abstractions;

public interface IScenarioLoader
{
    Task<IReadOnlyList<ExamScenario>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExamScenario>> LoadAsync(
        IEnumerable<string> patterns,
        CancellationToken cancellationToken = default);
}

public interface IAssertionEvaluator
{
    ScenarioResult Evaluate(ExamScenario scenario, AttemptEvidence evidence);
}

public interface IReportWriter
{
    Task WriteAsync(
        ExamReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
}
