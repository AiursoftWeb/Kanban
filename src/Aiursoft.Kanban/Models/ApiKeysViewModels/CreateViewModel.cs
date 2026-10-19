using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.ApiKeysViewModels;

[ExcludeFromCodeCoverage]
public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create API Key";
    }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 5479)]
    public int ExpirationDays { get; set; } = 365;

    public static int MaximumExpirationDays
    {
        get
        {
            var today = DateTime.UtcNow.Date;
            return (today.AddYears(15) - today).Days;
        }
    }
}
