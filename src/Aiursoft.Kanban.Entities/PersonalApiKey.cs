using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class PersonalApiKey
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    [MaxLength(64)]
    public required string KeyHash { get; set; }

    [MaxLength(15)]
    public required string Prefix { get; set; }

    public required string UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpirationTime { get; set; }
}
