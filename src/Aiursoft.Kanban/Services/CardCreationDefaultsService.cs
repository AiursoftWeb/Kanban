using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services;

public sealed class CardCreationDefaultsService(
    GlobalSettingsService settings,
    TimeProvider timeProvider) : IScopedDependency
{
    public async Task ApplyAsync(KanbanCard card)
    {
        if (card.DueDate == null &&
            await settings.GetBoolSettingAsync(SettingsMap.AutoSetDueDate))
        {
            var dueDateAdvanceDays = await settings.GetIntSettingAsync(SettingsMap.DueDateAdvanceDays);
            if (dueDateAdvanceDays > 0)
            {
                card.DueDate = timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(dueDateAdvanceDays);
            }
        }

        if (card.DueDate != null &&
            card.PlannedStartTime == null &&
            await settings.GetBoolSettingAsync(SettingsMap.AutoSetPlannedStartTime))
        {
            var plannedStartAdvanceDays =
                await settings.GetIntSettingAsync(SettingsMap.PlannedStartTimeAdvanceDays);
            if (plannedStartAdvanceDays > 0)
            {
                card.PlannedStartTime = card.DueDate.Value.AddDays(-plannedStartAdvanceDays);
            }
        }
    }
}
