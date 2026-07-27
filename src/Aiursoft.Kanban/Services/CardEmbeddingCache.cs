using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Util;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services;

/// <summary>
/// In-memory cache of KanbanCard embedding vectors for fast cosine-similarity search.
/// Partitioned by BoardId to efficiently filter cards a user has access to.
/// Loaded at startup and refreshed periodically via RefreshCardEmbeddingCacheJob.
/// </summary>
[ExcludeFromCodeCoverage]
public class CardEmbeddingCache(ILogger<CardEmbeddingCache> logger) : ISingletonDependency
{
    private const int MaxEntries = 10_000;

    // Dictionary<BoardId, Dictionary<CardId, float[]>>
    private Dictionary<int, Dictionary<int, float[]>> _cache = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get { lock (_lock) return _cache.Sum(kv => kv.Value.Count); }
    }

    /// <summary>
    /// Returns a snapshot of the current cache for search, merging vectors only for the specified boards.
    /// </summary>
    public Dictionary<int, float[]> SnapshotForBoards(IEnumerable<int> boardIds)
    {
        lock (_lock)
        {
            var result = new Dictionary<int, float[]>();
            foreach (var boardId in boardIds)
            {
                if (_cache.TryGetValue(boardId, out var boardCache))
                {
                    foreach (var kvp in boardCache)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }
            return result;
        }
    }

    public async Task LoadAsync(TemplateDbContext db)
    {
        var embeddings = await db.KanbanCards
            .AsNoTracking()
            .Include(c => c.Column)
            .Where(r => r.Embedding != null)
            .OrderByDescending(r => r.LastEmbeddedAt)
            .Select(r => new { r.Id, r.Column.BoardId, r.Embedding })
            .ToListAsync();

        int total = embeddings.Count;
        if (total > MaxEntries)
        {
            logger.LogWarning(
                "{Count} card embeddings exceed cache limit of {Limit}. Capping to most recent entries.",
                total, MaxEntries);
            embeddings = embeddings.Take(MaxEntries).ToList();
        }

        var newCache = new Dictionary<int, Dictionary<int, float[]>>();

        foreach (var item in embeddings)
        {
            var vector = EmbeddingHelper.Deserialize(item.Embedding!);
            if (vector != null)
            {
                if (!newCache.TryGetValue(item.BoardId, out var boardDict))
                {
                    boardDict = new Dictionary<int, float[]>();
                    newCache[item.BoardId] = boardDict;
                }
                boardDict[item.Id] = vector;
            }
            else
            {
                logger.LogWarning("Failed to deserialize embedding for card {CardId}: byte length {Length} is not a multiple of 4.",
                    item.Id, item.Embedding!.Length);
            }
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }
}
