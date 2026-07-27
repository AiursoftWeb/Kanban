using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

/// <summary>
/// Periodically reloads the in-memory <see cref="CardEmbeddingCache"/> from the database.
/// Only populates the cache if AI search is enabled in settings.
/// </summary>
public class RefreshCardEmbeddingCacheJob(
    IServiceScopeFactory scopeFactory,
    CardEmbeddingCache cache,
    GlobalSettingsService settingsService,
    ILogger<RefreshCardEmbeddingCacheJob> logger) : IBackgroundJob
{
    public string Name => "Refresh Card Embedding Cache";
    public string Description => "Reloads the in-memory card embedding cache from the database.";

    public async Task ExecuteAsync()
    {
        var enableAiSearch = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        var instance = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);

        if (!enableAiSearch)
        {
            logger.LogInformation("RefreshCardEmbeddingCacheJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("RefreshCardEmbeddingCacheJob: Ollama endpoint or model not configured. Skipping.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        await cache.LoadAsync(db);
        logger.LogInformation("RefreshCardEmbeddingCacheJob: Cache refreshed. {Count} embeddings loaded.", cache.Count);
    }
}
