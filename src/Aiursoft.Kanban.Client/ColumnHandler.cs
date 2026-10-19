using System.CommandLine;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;

namespace Aiursoft.Kanban.Client;

public sealed class ColumnHandler : NavigationCommandHandlerBuilder
{
    protected override string Name => "column";
    protected override string Description => "Create, update, move or delete board columns.";
    protected override CommandHandlerBuilder[] GetSubCommandHandlers() =>
        [new CreateHandler(), new UpdateHandler(), new MoveHandler(), new DeleteHandler()];

    private sealed class CreateHandler : KanbanCommandHandler
    {
        protected override string Name => "create";
        protected override string Description => "Create a column in a board.";
        private readonly Option<int> _boardId = new("--board-id") { Required = true };
        private readonly Option<string> _name = new("--name") { Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_boardId, _name];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.CreateColumnAsync(context.GetValue(_boardId), new CreateColumnRequest { Name = context.GetValue(_name)! });
    }

    private sealed class UpdateHandler : KanbanCommandHandler
    {
        protected override string Name => "update";
        protected override string Description => "Update a column's name or workflow status; omitted fields are unchanged.";
        private readonly Option<int> _id = new("--id") { Required = true };
        private readonly Option<string?> _name = new("--name");
        private readonly Option<string?> _status = new("--status") { Description = "NotStarted, InProgress or Completed." };
        protected override IEnumerable<Option> CommandOptions() => [_id, _name, _status];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.UpdateColumnAsync(context.GetValue(_id), new UpdateColumnRequest
            {
                Name = context.GetValue(_name), Status = context.GetValue(_status)
            });
    }

    private sealed class MoveHandler : KanbanCommandHandler
    {
        protected override string Name => "move";
        protected override string Description => "Move a column to a zero-based position within its board.";
        private readonly Option<int> _id = new("--id") { Required = true };
        private readonly Option<int> _order = new("--order") { Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_id, _order];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.MoveColumnAsync(context.GetValue(_id), context.GetValue(_order));
    }

    private sealed class DeleteHandler : KanbanCommandHandler
    {
        protected override string Name => "delete";
        protected override string Description => "Delete a column and its cards using the existing board permissions.";
        private readonly Option<int> _id = new("--id") { Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_id];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.DeleteColumnAsync(context.GetValue(_id));
    }
}
