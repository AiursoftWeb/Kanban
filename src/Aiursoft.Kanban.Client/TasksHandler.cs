using System.CommandLine;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.Kanban.SDK;

namespace Aiursoft.Kanban.Client;

public sealed class TasksHandler : KanbanCommandHandler
{
    protected override string Name => "tasks";
    protected override string Description => "List tasks assigned to the API key owner.";
    private readonly Option<string> _status = new("--status")
    {
        Description = "Task status filter, as supported by the existing API.", DefaultValueFactory = _ => "incomplete"
    };
    protected override IEnumerable<Option> CommandOptions() => [_status];
    protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
        await client.GetMyTasksAsync(status: context.GetValue(_status)!);
}
