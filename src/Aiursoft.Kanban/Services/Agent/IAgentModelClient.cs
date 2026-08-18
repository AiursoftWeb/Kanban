namespace Aiursoft.Kanban.Services.Agent;

/// <summary>
/// Sends model requests for the production Kanban agent.
/// </summary>
public interface IAgentModelClient
{
    Task<ClaudeResponse> SendAsync(
        string systemPrompt,
        List<ClaudeMessage> messages,
        List<ClaudeTool>? tools,
        CancellationToken cancellationToken = default,
        int maxTokens = 4096);
}
