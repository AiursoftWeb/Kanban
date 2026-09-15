using System.Text.Json;
using Aiursoft.AgentKit.AgentRunner;

namespace Aiursoft.Kanban.Services.Agent;

/// <summary>
/// Maps only unresolved AgentKit checkpoint calls to Kanban's existing Advice model.
/// The host retains presentation and persistence; this type does not execute or resume runs.
/// </summary>
internal sealed class KanbanCheckpointAdviceMapper(AdviceService adviceService)
{
    public IReadOnlyList<Advice> CreatePendingAdvice(
        Guid conversationId,
        OrderedAgentCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var advice = new List<Advice>();
        foreach (var resolution in checkpoint.Resolutions.Where(item => item.Result is null))
        {
            var parameters = ToParameters(resolution.Call.Arguments);
            advice.Add(adviceService.Create(
                conversationId,
                resolution.Call.Name,
                resolution.Call.Name,
                string.Empty,
                parameters,
                JsonSerializer.Serialize(parameters),
                resolution.Call.Id));
        }
        return advice;
    }

    private static Dictionary<string, object?> ToParameters(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool call arguments must be an object.", nameof(arguments));
        return arguments.EnumerateObject().ToDictionary(
            property => property.Name,
            property => Unwrap(property.Value),
            StringComparer.Ordinal);
    }

    private static object? Unwrap(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => UnwrapNumber(value),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static object UnwrapNumber(JsonElement value)
    {
        var raw = value.GetRawText();
        return raw.Contains('.', StringComparison.Ordinal) || raw.Contains('e', StringComparison.OrdinalIgnoreCase)
            ? value.GetDouble()
            : int.TryParse(raw, out var integer)
                ? integer
                : long.TryParse(raw, out var longInteger)
                    ? longInteger
                    : value.GetDouble();
    }
}
