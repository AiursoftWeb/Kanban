using System.CommandLine;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.Kanban.SDK;

namespace Aiursoft.Kanban.Client;

public sealed class BoardHandler : NavigationCommandHandlerBuilder
{
    protected override string Name => "board";
    protected override string Description => "Query your owned and shared boards.";
    protected override CommandHandlerBuilder[] GetSubCommandHandlers() => [new ListHandler(), new GetHandler()];

    private sealed class ListHandler : KanbanCommandHandler
    {
        protected override string Name => "list";
        protected override string Description => "List accessible boards.";
        protected override IEnumerable<Option> CommandOptions() => [];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.GetBoardsAsync();
    }

    private sealed class GetHandler : KanbanCommandHandler
    {
        protected override string Name => "get";
        protected override string Description => "Get a board, its columns and cards.";
        private readonly Option<int> _id = new("--id") { Description = "Board ID.", Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_id];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.GetBoardAsync(context.GetValue(_id));
    }
}
