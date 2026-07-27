using System.Net.Http.Headers;
using System.Text;
using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Util;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

public class GenerateCardEmbeddingsJob(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    GlobalSettingsService settingsService,
    RetryEngine retryEngine,
    ILogger<GenerateCardEmbeddingsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Card Embeddings";
    public string Description => "Generates vector embeddings for Kanban cards using the configured Ollama instance.";

    public async Task ExecuteAsync()
    {
        var enableAiSearch = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enableAiSearch)
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        var instance = await settingsService.GetEmbeddingEndpointAsync();
        if (string.IsNullOrWhiteSpace(instance))
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: Ollama endpoint not configured. Skipping.");
            return;
        }

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: EmbeddingModel not configured. Skipping.");
            return;
        }

        var token = await settingsService.GetEmbeddingTokenAsync();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var http = httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var successCount = 0;
        var lastId = 0;

        while (true)
        {
            var currentLastId = lastId;
            var pending = await db.KanbanCards
                .Where(c => c.Id > currentLastId && (c.Embedding == null || c.LastUpdatedAt > c.LastEmbeddedAt))
                .OrderBy(c => c.Id)
                .Take(100)
                .ToListAsync();

            if (pending.Count == 0) break;

            logger.LogInformation(
                "GenerateCardEmbeddingsJob: Generating embeddings for {Count} cards (from Id {LastId}) using model {Model} at {Endpoint}...",
                pending.Count, currentLastId, model, embedEndpoint);

            foreach (var card in pending)
                try
                {
                    await retryEngine.RunWithRetry(async _ =>
                    {
                        var rawText = $"{card.Title}\n{card.Description ?? ""}".Trim();
                        var maxChars = 8000;
                        float[]? embedding = null;

                        while (maxChars >= 500)
                        {
                            var input = TruncateForEmbedding(rawText, maxChars);
                            var requestBody = new { model, input };
                            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8,
                                "application/json");

                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            var response = await http.PostAsync(embedEndpoint, content, cts.Token);

                            if (response.IsSuccessStatusCode)
                            {
                                var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cts.Token);
                                if (result?.Embeddings is { Length: > 0 })
                                {
                                    embedding = result.Embeddings[0];
                                    break;
                                }
                            }

                            // If the input is too long, halve the limit and retry. Otherwise fail.
                            var errorBody = await response.Content.ReadAsStringAsync();
                            var isContextError = errorBody.Contains("context", StringComparison.OrdinalIgnoreCase) ||
                                                 errorBody.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                                                 errorBody.Contains("exceed", StringComparison.OrdinalIgnoreCase);
                            if (!isContextError || maxChars <= 500)
                            {
                                throw new HttpRequestException(
                                    $"Ollama embedding request failed for card #{card.Id} (HTTP {(int)response.StatusCode}): {errorBody}");
                            }

                            var prev = maxChars;
                            maxChars /= 2;
                            logger.LogWarning(
                                "Embedding input for card #{CardId} still too long at {Prev} chars, retrying with {Current} chars (binary fallback).",
                                card.Id, prev, maxChars);
                        }

                        if (embedding != null)
                        {
                            EmbeddingHelper.Normalize(embedding);
                            card.Embedding = EmbeddingHelper.Serialize(embedding);
                            card.LastEmbeddedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                            successCount++;
                        }
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "GenerateCardEmbeddingsJob: Failed to generate embedding for card #{CardId}.",
                        card.Id);
                }

            lastId = pending.Max(c => c.Id);
        }

        if (successCount > 0)
            logger.LogInformation("GenerateCardEmbeddingsJob: Successfully updated embeddings for {Count} cards.",
                successCount);
    }

    internal static string TruncateForEmbedding(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var head = (int)(maxChars * 0.75);
        var tail = maxChars - head - 5; // 5 for "\n...\n" separator
        if (tail <= 0) return text[..maxChars];
        return string.Concat(text.AsSpan(0, head), "\n...\n", text.AsSpan(text.Length - tail));
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")] public float[][]? Embeddings { get; set; }
    }
}
