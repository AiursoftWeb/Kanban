using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.Kanban.Models.ManageViewModels;

public class AIReportSettingsViewModel : UiStackLayoutViewModel
{
    public AIReportSettingsViewModel()
    {
        PageTitle = "AI Report Settings";
    }

    [Display(Name = "Daily Report Language")]
    [MaxLength(10)]
    public string? DailyReportLanguage { get; set; }

    [Display(Name = "Enable Daily Report")]
    public bool EnableDailyReport { get; set; } = true;

    [Display(Name = "Enable Weekly Report")]
    public bool EnableWeeklyReport { get; set; } = true;
}
