using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.Agent;

public sealed class AgentSessionHistoryService(
    TemplateDbContext db,
    TimeProvider timeProvider)
{
    public async Task SaveAsync(AgentConversation conversation)
    {
        var session = await db.AgentSessions.FindAsync(conversation.Id);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (session == null)
        {
            session = new AgentSession
            {
                Id = conversation.Id,
                UserId = conversation.UserId,
                BoardId = conversation.BoardId > 0 ? conversation.BoardId : null,
                Title = CreateTitle(conversation.Messages),
                State = conversation.State.ToString(),
                CreationTime = now,
                LastActivity = conversation.LastActivity,
                TranscriptJson = JsonConvert.SerializeObject(conversation.Messages)
            };
            db.AgentSessions.Add(session);
        }
        else
        {
            session.State = conversation.State.ToString();
            session.ErrorMessage = conversation.ErrorMessage;
            session.LastActivity = conversation.LastActivity;
            session.TranscriptJson = JsonConvert.SerializeObject(conversation.Messages);
        }
        await db.SaveChangesAsync();
    }

    public Task<List<AgentSessionSummary>> ListAsync(string userId) => db.AgentSessions
        .Where(session => session.UserId == userId)
        .OrderByDescending(session => session.LastActivity)
        .Select(session => new AgentSessionSummary(
            session.Id,
            session.Title,
            session.State,
            session.LastActivity,
            session.BoardId))
        .ToListAsync();

    public async Task<AgentConversation?> LoadAsync(Guid id, string userId)
    {
        var session = await db.AgentSessions.SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (session == null) return null;

        if (!Enum.TryParse<AgentState>(session.State, out var state))
            state = AgentState.Error;
        var wasInterrupted = state is AgentState.Thinking or AgentState.AwaitingApproval;
        if (wasInterrupted)
            state = AgentState.Error;

        return new AgentConversation
        {
            Id = session.Id,
            UserId = session.UserId,
            BoardId = session.BoardId ?? 0,
            State = state,
            ErrorMessage = wasInterrupted ? "Conversation was interrupted by a server restart." : session.ErrorMessage,
            LastActivity = session.LastActivity,
            Messages = JsonConvert.DeserializeObject<List<ToolMessagesItem>>(session.TranscriptJson) ?? []
        };
    }

    public async Task<AgentSessionSummary?> GetSummaryAsync(Guid id, string userId) => await db.AgentSessions
        .Where(session => session.Id == id && session.UserId == userId)
        .Select(session => new AgentSessionSummary(
            session.Id,
            session.Title,
            session.State,
            session.LastActivity,
            session.BoardId))
        .SingleOrDefaultAsync();

    private static string CreateTitle(IEnumerable<ToolMessagesItem> messages)
    {
        var question = messages.FirstOrDefault(message => message.Role == "user" && !message.IsMeta)?.Content ?? "New conversation";
        var normalized = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 160 ? normalized[..157] + "..." : normalized;
    }
}

public record AgentSessionSummary(Guid Id, string Title, string State, DateTime LastActivity, int? BoardId);
