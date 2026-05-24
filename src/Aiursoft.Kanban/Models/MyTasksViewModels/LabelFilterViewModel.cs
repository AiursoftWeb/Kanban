namespace Aiursoft.Kanban.Models.MyTasksViewModels;

public class LabelFilterViewModel
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required int UsageCount { get; init; }
}
