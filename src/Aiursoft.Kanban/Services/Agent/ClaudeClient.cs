using System.Text;
using System.Text.Json;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Agent;

public class ClaudeClient : ISingletonDependency
{
    private readonly AnthropicConfiguration _config;
    private readonly ILogger<ClaudeClient> _logger;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public ClaudeClient(IOptions<AnthropicConfiguration> config, ILogger<ClaudeClient> logger)
    {
        _config = config.Value;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<ClaudeResponse> SendAsync(
        string systemPrompt,
        List<ClaudeMessage> messages,
        List<ClaudeTool>? tools,
        CancellationToken ct = default,
        int maxTokens = 4096)
    {
        if (string.IsNullOrWhiteSpace(_config.CompletionApiUrl))
            throw new InvalidOperationException(
                "LLM CompletionApiUrl is not configured. Set AppSettings:Anthropic:CompletionApiUrl in appsettings.json.");

        var request = new ClaudeRequest
        {
            Model = _config.Model,
            MaxTokens = maxTokens,
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("Claude request: {Json}", json);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.CompletionApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_config.Token))
        {
            httpRequest.Headers.Add("x-api-key", _config.Token);
        }

        var response = await _http.SendAsync(httpRequest, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Claude API error ({StatusCode}): {Body}", (int)response.StatusCode, responseBody);
            var truncated = responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody;
            throw new HttpRequestException(
                $"Claude API returned {(int)response.StatusCode}: {truncated}");
        }

        _logger.LogDebug("Claude response: {Json}", responseBody);

        var result = JsonSerializer.Deserialize<ClaudeResponse>(responseBody, JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize Claude response.");
    }
}
