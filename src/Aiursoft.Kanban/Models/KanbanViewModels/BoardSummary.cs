namespace Aiursoft.Kanban.Models.KanbanViewModels;

public class BoardSummary
{
    public int BoardId { get; set; }
    public int TotalIncomplete { get; set; }
    public int TotalInProgress { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalOverdue { get; set; }
    public int TotalUnassigned { get; set; }
}
