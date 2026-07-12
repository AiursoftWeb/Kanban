namespace Aiursoft.Kanban.Services.Agent;

public interface IAgentService
{
    Task<Guid> StartRun(string userId, int boardId, string userMessage);
    Guid? ContinueRun(Guid conversationId, string userId, string userMessage);
    AgentConversation? GetConversation(Guid conversationId);
    void ApproveAdvice(Guid conversationId, Guid adviceId);
    void RejectAdvice(Guid conversationId, Guid adviceId);
    void ApproveAll(Guid conversationId);
    void CancelRun(Guid conversationId);
}
