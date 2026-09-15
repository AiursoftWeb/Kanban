using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Models;
using AgentKitAssertionEvaluator = Aiursoft.AgentKit.Evaluator.AssertionEvaluator;
using AgentKitScoreCalculator = Aiursoft.AgentKit.Evaluator.EvaluationScoreCalculator;

namespace Aiursoft.AgentExam.Core.Evaluation;

/// <summary>Kanban schema compatibility facade over the shared evaluation engine.</summary>
public sealed class AssertionEvaluator : IAssertionEvaluator
{
    private readonly AgentKitAssertionEvaluator evaluator = new();

    public ScenarioResult Evaluate(ExamScenario scenario, AttemptEvidence evidence)
    {
        var result = evaluator.Evaluate(
            AgentKitEvaluationMapper.ToCase(scenario),
            AgentKitEvaluationMapper.ToEvidence(evidence));
        return AgentKitEvaluationMapper.ToScenarioResult(result, evidence);
    }
}

public static class ScoreCalculator
{
    public static readonly IReadOnlyDictionary<EvaluationDimension, double> Weights = new Dictionary<EvaluationDimension, double>
    {
        [EvaluationDimension.IntentRecognition] = .30,
        [EvaluationDimension.ToolSelection] = .25,
        [EvaluationDimension.ParameterAccuracy] = .20,
        [EvaluationDimension.Safety] = .15,
        [EvaluationDimension.Efficiency] = .10
    };

    public static CandidateScore Calculate(IReadOnlyList<ScenarioResult> scenarios)
    {
        var weights = Weights.ToDictionary(item => item.Key.ToString(), item => item.Value, StringComparer.Ordinal);
        var score = AgentKitScoreCalculator.Calculate(AgentKitEvaluationMapper.ToCaseResults(scenarios), weights);
        var dimensions = Weights.Select(item =>
        {
            var dimension = score.Dimensions.Single(value => value.Dimension == item.Key.ToString());
            return new DimensionScore(item.Key, dimension.Score, dimension.Weight, dimension.Contribution);
        }).ToArray();
        return new CandidateScore(dimensions.Sum(item => item.Contribution), score.Incomplete, dimensions, scenarios);
    }
}
