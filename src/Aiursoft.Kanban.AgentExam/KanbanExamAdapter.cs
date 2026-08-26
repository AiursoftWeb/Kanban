using System.Diagnostics;
using System.Text.Json;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.Services.Agent;

namespace Aiursoft.Kanban.AgentExam;

public sealed class KanbanExamAdapter(
    KanbanExamScenarioSeeder seeder,
    KanbanExamStateSnapshotter snapshotter,
    IAgentService agentService)
{
    public Task<AttemptEvidence> RunAsync(
        ExamScenario scenario,
        CancellationToken cancellationToken = default) =>
        RunAsync(scenario, null, cancellationToken);

    public async Task<AttemptEvidence> RunAsync(
        ExamScenario scenario,
        string? systemPromptOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        KanbanExamAliasMap aliases;
        try
        {
            aliases = await seeder.SeedAsync(scenario.Setup, cancellationToken);
        }
        catch (Exception exception)
        {
            return new AttemptEvidence([], false, $"Scenario setup failed: {exception.Message}");
        }

        var evidence = new List<StepEvidence>();
        for (var stepIndex = 0; stepIndex < scenario.Steps.Length; stepIndex++)
        {
            var step = scenario.Steps[stepIndex];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(scenario.TimeoutSeconds));
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var execution = await agentService.RunDirectAsync(
                    aliases.GetUser(step.UserId),
                    aliases.GetBoard(step.BoardId),
                    step.UserMessage,
                    new AgentExecutionOptions
                    {
                        AutoApproveWrites = true,
                        SystemPromptOverride = systemPromptOverride
                    },
                    timeout.Token);
                stopwatch.Stop();
                var state = await snapshotter.CaptureAsync(aliases, timeout.Token);
                var response = execution.Conversation.Messages
                    .LastOrDefault(message => message.Role == "assistant" &&
                                              message.ToolCalls is not { Count: > 0 })
                    ?.Content ?? string.Empty;
                var valid = execution.Conversation.State != AgentState.Error;
                evidence.Add(new StepEvidence(
                    stepIndex,
                    step.UserId,
                    step.BoardId,
                    execution.ToolTraces.Select(trace => new ToolTrace(
                        trace.Name,
                        JsonSerializer.SerializeToElement(trace.Parameters),
                        ParseResult(trace.Result),
                        trace.Loop,
                        stopwatch.Elapsed)).ToArray(),
                    state,
                    response,
                    execution.Conversation.LoopCount,
                    stopwatch.Elapsed,
                    valid,
                    execution.Conversation.ErrorMessage));
                if (!valid)
                {
                    break;
                }
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                var state = await snapshotter.CaptureAsync(aliases, timeout.Token);
                evidence.Add(new StepEvidence(
                    stepIndex,
                    step.UserId,
                    step.BoardId,
                    [],
                    state,
                    string.Empty,
                    0,
                    stopwatch.Elapsed,
                    false,
                    exception.Message));
                break;
            }
        }

        var validAttempt = evidence.Count == scenario.Steps.Length && evidence.All(step => step.Valid);
        return new AttemptEvidence(
            evidence,
            validAttempt,
            validAttempt ? null : evidence.LastOrDefault()?.Error ?? "Attempt did not complete all steps.");
    }

    private static JsonElement ParseResult(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(result);
        }
    }
}
