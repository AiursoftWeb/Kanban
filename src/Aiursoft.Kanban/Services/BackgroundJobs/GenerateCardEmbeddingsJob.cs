using System.Net.Http.Json;
using System.Text;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Util;
using Aiursoft.Kanban.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.WebTools.Services;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

public class GenerateCardEmbeddingsJob(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    GlobalSettingsService settingsService,
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

        var instance = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
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

        var token = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingApiToken);
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var batchSize = 100;
        var cardsToEmbed = await db.KanbanCards
            .Where(c => c.Embedding == null || c.LastUpdatedAt > c.LastEmbeddedAt)
            .Take(batchSize)
            .ToListAsync();

        if (cardsToEmbed.Count == 0)
        {
            return;
        }

        logger.LogInformation("GenerateCardEmbeddingsJob: Generating embeddings for {Count} cards using model {Model} at {Endpoint}...", 
            cardsToEmbed.Count, model, embedEndpoint);

        var http = httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var successCount = 0;
        foreach (var card in cardsToEmbed)
        {
            try
            {
                var input = $"{card.Title}\n{card.Description ?? ""}".Trim();
                if (input.Length > 8000) input = input[..8000];

                var requestBody = new { model, input };
                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await http.PostAsync(embedEndpoint, content, cts.Token);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cts.Token);
                if (result?.Embeddings != null && result.Embeddings.Length > 0)
                {
                    var embedding = result.Embeddings[0];
                    EmbeddingHelper.Normalize(embedding);
                    card.Embedding = EmbeddingHelper.Serialize(embedding);
                    card.LastEmbeddedAt = DateTime.UtcNow;
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GenerateCardEmbeddingsJob: Failed to generate embedding for card #{CardId}.", card.Id);
            }
        }

        if (successCount > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("GenerateCardEmbeddingsJob: Successfully updated embeddings for {Count} cards.", successCount);
        }
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
