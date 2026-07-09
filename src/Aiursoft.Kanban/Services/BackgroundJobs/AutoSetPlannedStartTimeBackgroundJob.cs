using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.BackgroundJobs;

/// <summary>
/// Background job that runs every 30 minutes. When "Auto Set Planned Start Time"
/// is enabled, it scans all cards that have a due date but no planned start time,
/// and sets the planned start time to (due date - configured advance days).
/// </summary>
public class AutoSetPlannedStartTimeBackgroundJob : IBackgroundJob
{
    private readonly TemplateDbContext _db;
    private readonly GlobalSettingsService _settings;
    private readonly ILogger<AutoSetPlannedStartTimeBackgroundJob> _logger;

    public string Name => "Auto Set Planned Start Time";

    public string Description =>
        "Every 30 minutes, if enabled, scans cards with a due date but no planned " +
        "start time and sets their planned start time based on the configured advance days.";

    public AutoSetPlannedStartTimeBackgroundJob(
        TemplateDbContext db,
        GlobalSettingsService settings,
        ILogger<AutoSetPlannedStartTimeBackgroundJob> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var enabled = await _settings.GetBoolSettingAsync(SettingsMap.AutoSetPlannedStartTime);
        if (!enabled)
        {
            _logger.LogInformation(
                "AutoSetPlannedStartTime is disabled. Skipping.");
            return;
        }

        var advanceDays = await _settings.GetIntSettingAsync(SettingsMap.PlannedStartTimeAdvanceDays);
        if (advanceDays <= 0)
        {
            _logger.LogWarning(
                "PlannedStartTimeAdvanceDays is {Days} (non-positive). Skipping.", advanceDays);
            return;
        }

        _logger.LogInformation(
            "Scanning for cards with due date but no planned start time (advance days: {Days}).",
            advanceDays);

        var cardsToUpdate = await _db.KanbanCards
            .Where(c => c.DueDate != null && c.PlannedStartTime == null)
            .ToListAsync();

        if (cardsToUpdate.Count == 0)
        {
            _logger.LogInformation("No cards need planned start time update.");
            return;
        }

        _logger.LogInformation(
            "Found {Count} card(s) needing planned start time update.", cardsToUpdate.Count);

        var updated = 0;
        foreach (var card in cardsToUpdate)
        {
            card.PlannedStartTime = card.DueDate!.Value.AddDays(-advanceDays);
            updated++;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Updated planned start time for {Count} card(s).", updated);
    }
}
