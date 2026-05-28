namespace Aiursoft.Kanban.Configuration;

/// <summary>
/// Configuration for OpenAI/Ollama integration
/// </summary>
public class OpenAIConfiguration
{
    /// <summary>
    /// Authentication token for the API (can be empty for local Ollama)
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// API endpoint URL for completions
    /// </summary>
    public required string CompletionApiUrl { get; init; }

    /// <summary>
    /// Model name to use for completions
    /// </summary>
    public required string Model { get; init; }
}
