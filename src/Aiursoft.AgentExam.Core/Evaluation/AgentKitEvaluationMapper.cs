using System.Text.Json;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.AgentKit.Evaluator;
using AgentKitAssertion = Aiursoft.AgentKit.Evaluator.EvaluationAssertion;
using AgentKitCase = Aiursoft.AgentKit.Evaluator.EvaluationCase;
using AgentKitEvidence = Aiursoft.AgentKit.Evaluator.EvaluationEvidence;
using AgentKitStep = Aiursoft.AgentKit.Evaluator.EvaluationStep;
using AgentKitStepEvidence = Aiursoft.AgentKit.Evaluator.EvaluationStepEvidence;
using AgentKitToolTrace = Aiursoft.AgentKit.Evaluator.EvaluationToolTrace;

namespace Aiursoft.AgentExam.Core.Evaluation;

internal static class AgentKitEvaluationMapper
{
    public static AgentKitCase ToCase(ExamScenario scenario) => new(
        scenario.Id,
        scenario.Weight,
        scenario.Steps.Select((step, index) => new AgentKitStep(
            index,
            step.UserId,
            step.BoardId,
            new EvaluationExpectation(step.Expect.Trace
                .Concat(step.Expect.State)
                .Concat(step.Expect.Response)
                .Select(ToAssertion)
                .ToArray()))).ToArray());

    public static AgentKitEvidence ToEvidence(AttemptEvidence evidence) => new(
        evidence.Steps.Select(step => new AgentKitStepEvidence(
            step.StepIndex,
            step.UserId,
            step.BoardId,
            step.Tools.Select(tool => new AgentKitToolTrace(
                tool.Name,
                tool.Parameters.Clone(),
                tool.Result.Clone(),
                tool.Loop,
                tool.Elapsed)).ToArray(),
            step.State.Clone(),
            step.Response,
            step.LoopCount,
            step.Elapsed,
            step.Valid,
            step.Error)).ToArray(),
        evidence.Valid,
        evidence.Error);

    public static ScenarioResult ToScenarioResult(
        Aiursoft.AgentKit.Evaluator.EvaluationCaseResult result,
        AttemptEvidence originalEvidence) => new(
        result.CaseId,
        result.Weight,
        result.Passed,
        result.Valid,
        result.Assertions.Select(assertion => new AssertionResult(
            assertion.Id,
            Enum.Parse<EvaluationDimension>(assertion.Dimension, ignoreCase: false),
            assertion.Matched,
            assertion.Earned,
            assertion.Maximum,
            assertion.Required,
            assertion.HardFail,
            assertion.Detail)).ToArray(),
        NormalizeError(result.Error),
        originalEvidence.Steps);

    public static IReadOnlyList<Aiursoft.AgentKit.Evaluator.EvaluationCaseResult> ToCaseResults(
        IReadOnlyList<ScenarioResult> scenarios) => scenarios.Select(scenario => new Aiursoft.AgentKit.Evaluator.EvaluationCaseResult(
            scenario.ScenarioId,
            scenario.Weight,
            scenario.Passed,
            scenario.Valid,
            scenario.Assertions.Select(assertion => new Aiursoft.AgentKit.Evaluator.EvaluationAssertionResult(
                assertion.Id,
                assertion.Dimension.ToString(),
                assertion.Matched,
                assertion.Earned,
                assertion.Maximum,
                assertion.Required,
                assertion.HardFail,
                assertion.Detail)).ToArray(),
            scenario.Error)).ToArray();

    private static AgentKitAssertion ToAssertion(AssertionSpec assertion) => new(
        assertion.Id,
        assertion.Kind == AssertionKinds.MaxLoops
            ? EvaluationAssertionKinds.MaxIterations
            : assertion.Kind,
        assertion.Dimension.ToString(),
        assertion.Points,
        assertion.Penalty,
        assertion.Required,
        assertion.HardFail,
        assertion.Match.Clone(),
        assertion.Comment);

    private static string? NormalizeError(string? error) => error switch
    {
        "Evidence must contain each evaluation step index exactly once." =>
            "Evidence must contain each scenario step index exactly once.",
        "Evidence subject and context identities must match the evaluation step." =>
            "Evidence userId and boardId must match the scenario step.",
        _ => error
    };
}
