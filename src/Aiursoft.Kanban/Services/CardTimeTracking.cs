using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Services;

public static class CardTimeTracking
{
    public static void ApplyStatusChange(KanbanCard card, ColumnStatus from, ColumnStatus to, DateTime now)
    {
        // Reordering or moving between columns with the same status preserves corrected dates.
        if (from == to) return;
        switch (to)
        {
            case ColumnStatus.InProgress:
                card.ActualStartTime ??= now;
                card.ActualEndTime = null;
                break;
            case ColumnStatus.Completed:
                card.ActualStartTime ??= now;
                card.ActualEndTime = now;
                break;
        }
    }
}
