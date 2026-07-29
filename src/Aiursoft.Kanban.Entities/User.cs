using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]
public class User : IdentityUser
{
    public const string DefaultAvatarPath = "avatar/default-avatar.jpg";

    [MaxLength(30)]
    [MinLength(2)]
    public required string DisplayName { get; set; }

    [MaxLength(150)] [MinLength(2)] public string AvatarRelativePath { get; set; } = DefaultAvatarPath;

    [MaxLength(10)]
    public string DailyReportLanguage { get; set; } = "en";

    public bool EnableDailyReport { get; set; } = true;

    public bool EnableWeeklyReport { get; set; } = true;

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
