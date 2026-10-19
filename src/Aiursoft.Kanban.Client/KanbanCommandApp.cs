using Aiursoft.CommandFramework;

namespace Aiursoft.Kanban.Client;

public static class KanbanCommandApp
{
    public static NestedCommandApp Create() => new NestedCommandApp()
        .WithFeature(new BoardHandler())
        .WithFeature(new ColumnHandler())
        .WithFeature(new CardHandler())
        .WithFeature(new SearchHandler())
        .WithFeature(new TasksHandler());
}
