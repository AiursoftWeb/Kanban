using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aiursoft.Kanban.ExamRunner.Configuration;
using Aiursoft.Kanban.Services.Agent;

namespace Aiursoft.Kanban.ExamRunner.Transport;

public sealed class CandidateModelClient : IAgentModelClient, IDisposable
{
    private readonly Uri endpoint;
    private readonly string model;
    private readonly CandidateAuthentication authentication;
    private readonly string? credential;
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public CandidateModelClient(
        string endpoint,
        string model,
        CandidateAuthentication authentication,
        string? credential,
        HttpClient? httpClient = null)
    {
        this.endpoint = new Uri(endpoint, UriKind.Absolute);
        this.model = model;
        this.authentication = authentication;
        ValidateAuthentication(authentication, credential);
        this.credential = credential;
        this.httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        ownsClient = httpClient == null;
    }

    public async Task<ClaudeResponse> SendAsync(
        string systemPrompt,
        List<ClaudeMessage> messages,
        List<ClaudeTool>? tools,
        CancellationToken cancellationToken = default,
        int maxTokens = 4096)
    {
        var body = JsonSerializer.Serialize(new ClaudeRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            Stream = false
        }, Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyAuthentication(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Candidate model endpoint returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<ClaudeResponse>(responseBody, Options) ??
                throw new InvalidOperationException(
                    "Candidate model endpoint returned an empty Messages response.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Candidate model endpoint returned an invalid Messages response.",
                exception);
        }
    }

    private static void ValidateAuthentication(
        CandidateAuthentication authentication,
        string? credential)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        if (authentication.Mode is not ("none" or "apiKey" or "bearer"))
        {
            throw new ArgumentException("Unknown candidate authentication mode.", nameof(authentication));
        }
        if (authentication.Mode == "none" && credential != null)
        {
            throw new ArgumentException(
                "Authentication credential is not allowed in none mode.",
                nameof(credential));
        }
        if (authentication.Mode != "none" && string.IsNullOrWhiteSpace(credential))
        {
            throw new ArgumentException(
                "Authentication credential is required for the configured mode.",
                nameof(credential));
        }
    }

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        if (authentication.Mode == "apiKey")
        {
            request.Headers.Add("x-api-key", credential);
        }
        else if (authentication.Mode == "bearer")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }
}
