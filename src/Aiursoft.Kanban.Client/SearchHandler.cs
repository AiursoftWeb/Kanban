using System.CommandLine;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.Kanban.SDK;

namespace Aiursoft.Kanban.Client;

public sealed class SearchHandler : KanbanCommandHandler
{
    protected override string Name => "search";
    protected override string Description => "Search cards accessible to the API key owner.";
    private readonly Option<string> _query = new("--query") { Required = true };
    protected override IEnumerable<Option> CommandOptions() => [_query];
    protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
        await client.SearchCardsAsync(context.GetValue(_query)!);
}
