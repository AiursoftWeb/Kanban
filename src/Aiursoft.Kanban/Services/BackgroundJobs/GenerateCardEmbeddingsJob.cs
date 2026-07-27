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
    internal const int MaxDocumentsPerRun = 50;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public string Name => "Generate Card Embeddings";
    public string Description => "Generates vector embeddings for Kanban cards using the configured Ollama instance.";

    public async Task ExecuteAsync()
    {
        if (!await RunLock.WaitAsync(0))
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: previous run is still active. Skipping.");
            return;
        }

        try
        {
            await ExecuteCoreAsync();
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task ExecuteCoreAsync()
    {
        if (!await settingsService.IsAiSearchEnabledAsync())
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: Embedding endpoint not configured. Skipping.");
            return;
        }

        var enabled = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enabled)
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("GenerateCardEmbeddingsJob: EmbeddingModel not configured. Skipping.");
            return;
        }

        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        var token    = await settingsService.GetEmbeddingTokenAsync();
        var baseUri = new Uri(endpoint);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var http = httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var lastId = 0;
        var attempted = 0;
        var succeeded = 0;

        while (true)
        {
            if (attempted >= MaxDocumentsPerRun)
            {
                logger.LogInformation(
                    "GenerateCardEmbeddingsJob: attempted {Count} cards, stopping until next run.",
                    attempted);
                break;
            }

            var currentLastId = lastId;
            var take = Math.Min(10, MaxDocumentsPerRun - attempted);
            var pending = await db.KanbanCards
                .Where(c => c.Id > currentLastId && (c.Embedding == null || c.LastUpdatedAt > c.LastEmbeddedAt))
                .OrderBy(c => c.Id)
                .Take(take)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var card in pending)
            {
                attempted++;
                try
                {
                    var sourceUpdatedAt = card.LastUpdatedAt;
                    float[]? embedding = null;
                    await retryEngine.RunWithRetry(async _ =>
                    {
                        embedding = await CallEmbedApiAsync(embedEndpoint, model, http, card);
                    });

                    if (await TrySaveEmbeddingIfCardUnchangedAsync(db, card, sourceUpdatedAt, embedding!))
                    {
                        succeeded++;
                    }
                    else
                    {
                        logger.LogInformation(
                            "GenerateCardEmbeddingsJob: card #{CardId} changed while embedding was running. Skipping stale result.",
                            card.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "GenerateCardEmbeddingsJob: Failed to generate embedding for card #{CardId}.",
                        card.Id);
                }
            }

            lastId = pending.Max(c => c.Id);
        }

        logger.LogInformation(
            "GenerateCardEmbeddingsJob: done. {Succeeded}/{Attempted} cards processed.",
            succeeded, attempted);
    }

    private async Task<float[]> CallEmbedApiAsync(
        string embedEndpoint, string model, HttpClient http, KanbanCard card)
    {
        var rawText = $"{card.Title}\n{card.Description ?? ""}".Trim();
        var maxChars = 8000;

        while (maxChars >= 500)
        {
            var input = TruncateForEmbedding(rawText, maxChars);
            var requestBody = new { model, input };
            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8,
                "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await http.PostAsync(embedEndpoint, content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cts.Token);
                if (result?.Embeddings is { Length: > 0 })
                {
                    var embedding = result.Embeddings[0];
                    EmbeddingHelper.Normalize(embedding);
                    return embedding;
                }

                throw new InvalidOperationException($"Ollama returned no embeddings for card #{card.Id}.");
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

        throw new InvalidOperationException($"Failed to generate embedding for card #{card.Id} after all retries.");
    }

    internal static async Task<bool> TrySaveEmbeddingIfCardUnchangedAsync(
        TemplateDbContext db,
        KanbanCard card,
        DateTime sourceUpdatedAt,
        float[] embedding)
    {
        var serialized = EmbeddingHelper.Serialize(embedding);
        if (db.Database.IsRelational())
        {
            var updated = await db.KanbanCards
                .Where(c => c.Id == card.Id && c.LastUpdatedAt == sourceUpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Embedding, serialized)
                    .SetProperty(c => c.LastEmbeddedAt, sourceUpdatedAt));
            return updated == 1;
        }

        await db.Entry(card).ReloadAsync();
        if (db.Entry(card).State == EntityState.Detached || card.LastUpdatedAt != sourceUpdatedAt)
        {
            return false;
        }

        card.Embedding      = serialized;
        card.LastEmbeddedAt = sourceUpdatedAt;
        await db.SaveChangesAsync();
        return true;
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
