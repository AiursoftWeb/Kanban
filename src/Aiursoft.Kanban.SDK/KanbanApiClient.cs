using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Services;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.SDK;

public sealed class KanbanApiClient(
    AiurProtocolClient http,
    IOptions<KanbanApiOptions> options,
    IKanbanAccessTokenProvider tokenProvider)
{
    private readonly string _endpoint = options.Value.Endpoint.TrimEnd('/');

    public Task<MobileConfigurationResponse> GetConfigurationAsync() =>
        http.Get<MobileConfigurationResponse>(Endpoint("/api/v1/config"));

    public Task<LocalAuthenticationResponse> LoginLocalAsync(LocalLoginRequest request) =>
        http.Post<LocalAuthenticationResponse>(Endpoint("/api/v1/auth/local/login"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody);

    public Task<LocalAuthenticationResponse> RegisterLocalAsync(LocalRegistrationRequest request) =>
        http.Post<LocalAuthenticationResponse>(Endpoint("/api/v1/auth/local/register"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody);

    public async Task<BoardListResponse> GetBoardsAsync() =>
        await http.Get<BoardListResponse>(Endpoint("/api/v1/boards"), headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> GetBoardAsync(int boardId) =>
        await http.Get<BoardResponse>(Endpoint($"/api/v1/boards/{boardId}"), headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> CreateBoardAsync(CreateBoardRequest request) =>
        await http.Post<BoardResponse>(Endpoint("/api/v1/boards"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> CreateColumnAsync(int boardId, CreateColumnRequest request) =>
        await http.Post<BoardResponse>(Endpoint($"/api/v1/boards/{boardId}/columns"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardResponse> CreateCardAsync(int columnId, CreateCardRequest request) =>
        await http.Post<CardResponse>(Endpoint($"/api/v1/columns/{columnId}/cards"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardResponse> MoveCardAsync(int cardId, MoveCardRequest request) =>
        await http.Put<CardResponse>(Endpoint($"/api/v1/cards/{cardId}/position"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    private AiurApiEndpoint Endpoint(string route) => new(_endpoint, route, new { });

    private async Task<IDictionary<string, string>> AuthorizationHeadersAsync()
    {
        var token = await tokenProvider.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new Dictionary<string, string>();
        }

        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        };
    }
}
