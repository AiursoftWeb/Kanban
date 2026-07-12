using System.Text;
using System.Text.Json;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Kanban.Services.Agent;

public class ClaudeClient : ISingletonDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ClaudeClient> _logger;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public ClaudeClient(IServiceProvider serviceProvider, ILogger<ClaudeClient> logger)
    {
        _serviceProvider = serviceProvider;
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
        // Resolve per-call so settings changes take effect without restart
        var globalSettings = _serviceProvider.GetRequiredService<GlobalSettingsService>();
        var endpoint = await globalSettings.GetSettingValueAsync(SettingsMap.AnthropicChatEndpoint);
        var model = await globalSettings.GetSettingValueAsync(SettingsMap.AnthropicModel);
        var token = await globalSettings.GetSettingValueAsync(SettingsMap.AnthropicApiToken);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "Anthropic API Endpoint is not configured. Set it in Admin → Global Settings → Anthropic API Endpoint.");

        var request = new ClaudeRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        _logger.LogDebug("Claude request: {Json}", json);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            httpRequest.Headers.Add("x-api-key", token);
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
