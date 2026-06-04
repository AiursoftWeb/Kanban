using System.Collections.Concurrent;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Agent;

public class AdviceService : ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, Advice> _advice = new();

    public Advice Create(
        Guid conversationId,
        string toolName,
        string toolDisplayName,
        string toolDescription,
        Dictionary<string, object?> parameters,
        string parameterDisplay,
        string? toolCallId = null)
    {
        var advice = new Advice
        {
            ConversationId = conversationId,
            ToolName = toolName,
            ToolDisplayName = toolDisplayName,
            ToolDescription = toolDescription,
            Parameters = parameters,
            ParameterDisplay = parameterDisplay,
            ToolCallId = toolCallId
        };
        _advice[advice.Id] = advice;
        return advice;
    }

    public Advice? Get(Guid adviceId)
    {
        _advice.TryGetValue(adviceId, out var advice);
        return advice;
    }

    public List<Advice> GetPendingForConversation(Guid conversationId)
    {
        return _advice.Values
            .Where(a => a.ConversationId == conversationId && a.Status == AdviceStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToList();
    }

    public void UpdateStatus(Guid adviceId, AdviceStatus status)
    {
        if (_advice.TryGetValue(adviceId, out var advice))
        {
            advice.Status = status;
        }
    }

    public void SetResult(Guid adviceId, string? result, string? error)
    {
        if (_advice.TryGetValue(adviceId, out var advice))
        {
            advice.Result = result;
            advice.Error = error;
            advice.Status = error != null ? AdviceStatus.Failed : AdviceStatus.Executed;
        }
    }

    public void RemoveConversationAdvice(Guid conversationId)
    {
        var toRemove = _advice.Values
            .Where(a => a.ConversationId == conversationId)
            .Select(a => a.Id)
            .ToList();
        foreach (var id in toRemove)
        {
            _advice.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Removes advice items older than the cutoff that belong to conversations
    /// no longer in memory (already removed via CancelRun). Called by the lazy
    /// cleanup sweep so orphaned advice doesn't accumulate.
    /// </summary>
    public void RemoveExpiredAdvice(DateTime cutoff)
    {
        var toRemove = _advice.Values
            .Where(a => a.CreatedAt < cutoff)
            .Select(a => a.Id)
            .ToList();
        foreach (var id in toRemove)
        {
            _advice.TryRemove(id, out _);
        }
    }
}
