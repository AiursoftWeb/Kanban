using Aiursoft.GptClient.Services;
using Aiursoft.Kanban.Configuration;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services;

/// <summary>
/// Service for interacting with Ollama AI
/// </summary>
public interface IOllamaService
{
    /// <summary>
    /// Ask a question to Ollama and get the response
    /// </summary>
    /// <param name="question">The question to ask</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI-generated response</returns>
    Task<string> AskQuestion(string question, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for interacting with Ollama AI using GptClient
/// </summary>
public class OllamaService : IOllamaService
{
    private readonly ChatClient _chatClient;
    private readonly OpenAIConfiguration _configuration;

    public OllamaService(ChatClient chatClient, IOptions<OpenAIConfiguration> configuration)
    {
        _chatClient = chatClient;
        _configuration = configuration.Value;
    }

    /// <inheritdoc/>
    public async Task<string> AskQuestion(string question, CancellationToken cancellationToken = default)
    {
        var response = await _chatClient.AskString(
            modelType: _configuration.Model,
            completionApiUrl: _configuration.CompletionApiUrl,
            token: _configuration.Token,
            content: [question],
            cancellationToken: cancellationToken);

        return response.GetAnswerPart();
    }
}
