using System.Security.Cryptography;
using System.Text;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Util;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services;

public class CardVectorSearchService(
    TemplateDbContext db,
    CardEmbeddingCache cache,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<CardVectorSearchService> logger) : IScopedDependency
{
    private const int EmbedTimeoutSeconds = 10;
    internal static readonly TimeSpan AccessThrottle = TimeSpan.FromHours(1);

    public async Task<(bool UsedAi, List<KanbanCard> Results, int TotalCount)> SearchAsync(
        IQueryable<KanbanCard> baseQuery,
        string query,
        IEnumerable<int> allowedBoardIds,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (!await settingsService.IsAiSearchEnabledAsync())
        {
            return (false, [], 0);
        }

        var snapshot = cache.SnapshotForBoards(allowedBoardIds);
        if (snapshot.Count == 0)
        {
            return (false, [], 0);
        }

        float[]? queryVector;
        try
        {
            var expectedDimension = snapshot.Values.First().Length;
            queryVector = await EmbedQueryAsync(query, expectedDimension, ct);
        }
        catch (Exception)
        {
            return (false, [], 0);
        }

        if (queryVector == null)
        {
            return (false, [], 0);
        }

        var scored = new List<(int CardId, float Score)>();
        var skippedDimensionMismatch = 0;
        foreach (var kv in snapshot)
        {
            if (kv.Value.Length != queryVector.Length)
            {
                skippedDimensionMismatch++;
                continue;
            }
            var score = EmbeddingHelper.CosineSimilarity(queryVector, kv.Value);
            if (score > 0)
            {
                scored.Add((kv.Key, score));
            }
        }

        if (scored.Count == 0 && skippedDimensionMismatch > 0)
        {
            logger.LogWarning(
                "Vector search skipped {Count} card embeddings because their dimensions did not match the query vector.",
                skippedDimensionMismatch);
            return (false, [], 0);
        }

        scored = scored.OrderByDescending(x => x.Score).ToList();
        var total = scored.Count;
        var topIds = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.CardId)
            .ToList();

        if (topIds.Count == 0)
        {
            return (true, [], total);
        }

        var cards = await baseQuery
            .Where(d => topIds.Contains(d.Id))
            .ToListAsync(ct);

        var cardMap = cards.ToDictionary(d => d.Id);
        var ordered = topIds
            .Select(id => cardMap.GetValueOrDefault(id))
            .Where(d => d != null)
            .Cast<KanbanCard>()
            .ToList();

        return (true, ordered, total);
    }



    private static string ComputeQueryCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(40);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
            if (sb.Length >= 40) break;
        }
        return sb.ToString();
    }

    private async Task<float[]?> EmbedQueryAsync(string text, int expectedDimension, CancellationToken ct)
    {
        var cacheKey = ComputeQueryCacheKey(text);

        var cached = await db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == cacheKey, ct);

        if (cached != null)
        {
            var vector = EmbeddingHelper.Deserialize(cached.Embedding);
            if (vector != null && vector.Length == expectedDimension)
            {
                var now = DateTime.UtcNow;
                if (now - cached.LastAccessedAt >= AccessThrottle)
                {
                    cached.LastAccessedAt = now;
                    await db.SaveChangesAsync(ct);
                }

                return vector;
            }

            db.SearchEmbeddings.Remove(cached);
            await db.SaveChangesAsync(ct);
        }

        var instance = await settingsService.GetEmbeddingEndpointAsync();
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token = await settingsService.GetEmbeddingTokenAsync();

        const int maxQueryChars = 8000;
        var input = text.Length > maxQueryChars ? text[..maxQueryChars] : text;

        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";
        var requestBody = new { model, input };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, embedEndpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(EmbedTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var response = await http.SendAsync(request, linkedCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: linkedCts.Token);
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
        {
            return null;
        }

        var embedding = result.Embeddings[0];
        EmbeddingHelper.Normalize(embedding);

        var serialized = EmbeddingHelper.Serialize(embedding);
        try
        {
            var now = DateTime.UtcNow;
            db.SearchEmbeddings.Add(new SearchEmbedding
            {
                QueryText = cacheKey,
                Embedding = serialized,
                CreatedAt = now,
                LastAccessedAt = now
            });
            await db.SaveChangesAsync(ct);

            await TrimCacheAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Failed to cache query embedding for '{Query}'. Likely a concurrent duplicate.", text);
        }

        return embedding;
    }

    private async Task TrimCacheAsync(CancellationToken ct)
    {
        var limit = await settingsService.GetIntSettingAsync(SettingsMap.EmbeddingQueryCacheLimit);
        if (limit <= 0) limit = 2000;

        var count = await db.SearchEmbeddings.CountAsync(ct);
        if (count <= limit) return;

        var toDelete = await db.SearchEmbeddings
            .OrderBy(e => e.LastAccessedAt)
            .Take(count - limit)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.SearchEmbeddings.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
        }
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
