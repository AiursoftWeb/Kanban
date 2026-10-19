using System.CommandLine;
using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Exceptions;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Client;

public abstract class KanbanCommandHandler : ExecutableCommandHandlerBuilder
{
    private readonly Option<string> _server = new("--server")
    {
        Description = "Kanban server base URL.", Required = true
    };
    private readonly Option<string> _apiKey = new("--api-key")
    {
        Description = "Personal API key for this invocation. Never saved.", Required = true
    };
    private readonly Option<bool> _json = new("--json")
    {
        Description = "Write the complete AiurProtocol response as JSON."
    };

    protected override IEnumerable<Option> GetCommandOptions() => [_server, _apiKey, _json, .. CommandOptions()];
    protected abstract IEnumerable<Option> CommandOptions();
    protected abstract Task<AiurResponse> InvokeAsync(KanbanApiClient client, ParseResult context);

    protected override async Task Execute(ParseResult context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKanbanSdk(context.GetValue(_server)!);
        services.AddScoped<IKanbanAccessTokenProvider>(_ => new PersonalKeyProvider(context.GetValue(_apiKey)!));
        // Match S360 and AlertCenter: credentials must not follow redirects.
        services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        using var provider = services.BuildServiceProvider();
        AiurResponse response;
        try
        {
            response = await InvokeAsync(provider.GetRequiredService<KanbanApiClient>(), context);
        }
        catch (AiurUnexpectedServerResponseException exception)
        {
            var reasons = exception is AiurBadApiInputException input ? " " + string.Join("; ", input.Reasons) : string.Empty;
            throw new InvalidOperationException($"{exception.Response.Code} ({(int)exception.Response.Code}): {exception.Message}{reasons}");
        }
        var output = context.InvocationConfiguration.Output;
        if (context.GetValue(_json))
        {
            output.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented, ProtocolSettings.JsonSettings));
            return;
        }
        output.WriteLine(response.Message);
        switch (response)
        {
            case BoardListResponse boards:
                foreach (var board in boards.Boards)
                    output.WriteLine($"{board.Id}  {board.Name}  Cards: {board.CardCount}");
                break;
            case BoardResponse board:
                output.WriteLine($"Board: {board.Board.Id}  {board.Board.Name}");
                foreach (var column in board.Board.Columns)
                {
                    output.WriteLine($"Column: {column.Id}  {column.Name}  {column.Status}");
                    foreach (var card in column.Cards)
                        output.WriteLine($"  {card.Id}  {card.Title}  {card.Priority}");
                }
                break;
            case CardResponse card:
                output.WriteLine($"{card.Card.Id}  {card.Card.Title}  Column: {card.Card.ColumnId}");
                break;
            case CardDetailsResponse details:
                output.WriteLine($"{details.Card.Id}  {details.Card.Title}  {details.Card.Priority}");
                output.WriteLine($"Board: {details.Card.BoardId}  Column: {details.Card.ColumnId}");
                output.WriteLine(details.Card.Description);
                foreach (var comment in details.Card.Comments)
                    output.WriteLine($"Comment {comment.Id}  {comment.Author.DisplayName}: {comment.Content}");
                break;
            case CardCommentResponse comment:
                output.WriteLine($"Comment {comment.Comment.Id}: {comment.Comment.Content}");
                break;
            case CardSearchResponse search:
                output.WriteLine($"Results: {search.TotalCount}");
                foreach (var card in search.Cards)
                    output.WriteLine($"{card.Id}  {card.Title}  {card.Status}  {card.BoardName}");
                break;
            case MyTasksResponse tasks:
                foreach (var card in tasks.Cards)
                    output.WriteLine($"{card.Id}  {card.Title}  {card.Status}  {card.BoardName}");
                break;
        }
    }

    private sealed class PersonalKeyProvider(string key) : IKanbanAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(key);
    }
}
