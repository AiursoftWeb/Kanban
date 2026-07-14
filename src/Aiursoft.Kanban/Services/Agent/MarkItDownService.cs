using System.Text.Json;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Agent;

public class MarkItDownService : ISingletonDependency
{
    private readonly IOptions<AppSettings> _appSettings;
    private readonly ILogger<MarkItDownService> _logger;
    private readonly HttpClient _http;

    public MarkItDownService(IOptions<AppSettings> appSettings, ILogger<MarkItDownService> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<string> ConvertExcelToMarkdownAsync(
        Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var endpoint = _appSettings.Value.MarkItDown.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("MarkItDown endpoint is not configured.");

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(streamContent, "file", fileName);

        var response = await _http.PostAsync(endpoint, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("MarkItDown API error ({StatusCode}): {Body}",
                (int)response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"MarkItDown conversion failed: {(int)response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<MarkItDownResponse>(responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result == null || string.IsNullOrWhiteSpace(result.Markdown))
            throw new InvalidOperationException("MarkItDown returned empty markdown.");

        return result.Markdown;
    }

    private class MarkItDownResponse
    {
        public string Status { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Markdown { get; set; } = "";
    }
}
