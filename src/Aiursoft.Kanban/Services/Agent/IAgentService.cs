namespace Aiursoft.Kanban.Services.Agent;

public interface IAgentService
{
    Guid StartRun(string userId, int boardId, string userMessage);
    AgentConversation? GetConversation(Guid conversationId);
    void ApproveAdvice(Guid conversationId, Guid adviceId);
    void RejectAdvice(Guid conversationId, Guid adviceId);
    void ApproveAll(Guid conversationId);
    void CancelRun(Guid conversationId);
}
