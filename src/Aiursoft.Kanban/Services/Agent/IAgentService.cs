namespace Aiursoft.Kanban.Services.Agent;

public interface IAgentService
{
    Task<Guid> StartRun(string userId, int boardId, string userMessage, string? excelMarkdown = null);
    Task<AgentExecutionResult> RunDirectAsync(
        string userId,
        int boardId,
        string userMessage,
        AgentExecutionOptions options,
        CancellationToken cancellationToken = default);
    Guid? ContinueRun(Guid conversationId, string userId, string userMessage, string? excelMarkdown = null);
    AgentConversation? GetConversation(Guid conversationId);
    void ApproveAdvice(Guid conversationId, Guid adviceId);
    void RejectAdvice(Guid conversationId, Guid adviceId);
    void ApproveAll(Guid conversationId);
    void CancelRun(Guid conversationId);
}
