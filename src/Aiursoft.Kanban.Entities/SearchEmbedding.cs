using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

/// <summary>
/// Database cache for user-query embedding vectors (circular LRU buffer).
/// Avoids redundant round-trips to the embedding model for repeated search terms.
/// </summary>
[ExcludeFromCodeCoverage]
public class SearchEmbedding
{
    public int Id { get; set; }

    /// <summary>
    /// The search query text (truncated to 40 chars before embedding).
    /// </summary>
    public required string QueryText { get; set; }

    /// <summary>
    /// Serialized normalized float[] vector (4 bytes × N dims, little-endian).
    /// </summary>
    public byte[] Embedding { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}
