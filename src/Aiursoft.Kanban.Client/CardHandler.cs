using System.CommandLine;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;

namespace Aiursoft.Kanban.Client;

public sealed class CardHandler : NavigationCommandHandlerBuilder
{
    protected override string Name => "card";
    protected override string Description => "Read, create, update, move and comment on cards.";
    protected override CommandHandlerBuilder[] GetSubCommandHandlers() =>
        [new GetHandler(), new CreateHandler(), new UpdateHandler(), new MoveHandler(), new CommentHandler()];

    private sealed class GetHandler : KanbanCommandHandler
    {
        protected override string Name => "get";
        protected override string Description => "Read card details and comments.";
        private readonly Option<int> _id = new("--id") { Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_id];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.GetCardDetailsAsync(context.GetValue(_id));
    }

    private sealed class CreateHandler : KanbanCommandHandler
    {
        protected override string Name => "create";
        protected override string Description => "Create a card in a column.";
        private readonly Option<int> _columnId = new("--column-id") { Required = true };
        private readonly Option<string> _title = new("--title") { Required = true };
        private readonly Option<string?> _description = new("--description");
        protected override IEnumerable<Option> CommandOptions() => [_columnId, _title, _description];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.CreateCardAsync(context.GetValue(_columnId), new CreateCardRequest
            {
                Title = context.GetValue(_title)!, Description = context.GetValue(_description)
            });
    }

    private sealed class UpdateHandler : KanbanCommandHandler
    {
        protected override string Name => "update";
        protected override string Description => "Update a card; omitted fields retain their current values.";
        private readonly Option<int> _id = new("--id") { Required = true };
        private readonly Option<string?> _title = new("--title");
        private readonly Option<string?> _description = new("--description") { Description = "Pass an empty string to clear the description." };
        private readonly Option<string?> _priority = new("--priority") { Description = "Urgent, High, Medium, Low or None." };
        private readonly Option<string?> _assignee = new("--assigned-user-id") { Description = "User ID; pass an empty string to unassign." };
        private readonly Option<DateTimeOffset?> _plannedStart = new("--planned-start") { Description = "ISO 8601 timestamp with UTC offset." };
        private readonly Option<DateTimeOffset?> _dueDate = new("--due-date") { Description = "ISO 8601 timestamp with UTC offset." };
        protected override IEnumerable<Option> CommandOptions() =>
            [_id, _title, _description, _priority, _assignee, _plannedStart, _dueDate];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context)
        {
            var id = context.GetValue(_id);
            var current = (await client.GetCardDetailsAsync(id)).Card;
            return await client.UpdateCardAsync(id, new UpdateCardRequest
            {
                Title = context.GetValue(_title) ?? current.Title,
                Description = context.GetValue(_description) ?? current.Description,
                Priority = context.GetValue(_priority) ?? current.Priority,
                AssignedUserId = context.GetValue(_assignee) ?? current.AssignedUser?.Id,
                PlannedStartTime = context.GetValue(_plannedStart)?.UtcDateTime ?? current.PlannedStartTime,
                DueDate = context.GetValue(_dueDate)?.UtcDateTime ?? current.DueDate,
                RecurrenceInterval = current.RecurrenceInterval,
                RecurrenceUnit = current.RecurrenceUnit
            });
        }
    }

    private sealed class MoveHandler : KanbanCommandHandler
    {
        protected override string Name => "move";
        protected override string Description => "Move a card to a column and zero-based position within its board.";
        private readonly Option<int> _id = new("--id") { Required = true };
        private readonly Option<int> _columnId = new("--column-id") { Required = true };
        private readonly Option<int> _order = new("--order") { Description = "Zero-based position in the target column.", DefaultValueFactory = _ => 0 };
        protected override IEnumerable<Option> CommandOptions() => [_id, _columnId, _order];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.MoveCardAsync(context.GetValue(_id), new MoveCardRequest
            {
                TargetColumnId = context.GetValue(_columnId), NewOrder = context.GetValue(_order)
            });
    }

    private sealed class CommentHandler : KanbanCommandHandler
    {
        protected override string Name => "comment";
        protected override string Description => "Add a text comment to a card.";
        private readonly Option<int> _id = new("--id") { Required = true };
        private readonly Option<string> _content = new("--content") { Required = true };
        protected override IEnumerable<Option> CommandOptions() => [_id, _content];
        protected override async Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context) =>
            await client.AddCardCommentAsync(context.GetValue(_id), new AddCardCommentRequest { Content = context.GetValue(_content)! });
    }
}
