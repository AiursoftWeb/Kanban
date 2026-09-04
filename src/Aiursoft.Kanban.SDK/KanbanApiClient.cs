using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Services;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Aiursoft.Kanban.SDK;

public sealed class KanbanApiClient(
    AiurProtocolClient http,
    IHttpClientFactory clientFactory,
    IOptions<KanbanApiOptions> options,
    IKanbanAccessTokenProvider tokenProvider) : IDisposable
{
    private const int MaxDownloadedImageBytes = 12 * 1024 * 1024;
    private readonly string _endpoint = options.Value.Endpoint.TrimEnd('/');
    private readonly HttpClient _rawHttp = clientFactory.CreateClient();

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

    public async Task<GanttResponse> GetGanttAsync(int boardId) =>
        await http.Get<GanttResponse>(Endpoint($"/api/v1/boards/{boardId}/gantt"),
            headers: await AuthorizationHeadersAsync());

    public async Task<ArchivedBoardListResponse> GetArchivedBoardsAsync() =>
        await http.Get<ArchivedBoardListResponse>(Endpoint("/api/v1/boards/archived"),
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardArchiveResponse> SetBoardArchivedAsync(int boardId, bool archive) =>
        await http.Put<BoardArchiveResponse>(Endpoint($"/api/v1/boards/{boardId}/archive"),
            new AiurApiPayload(new SetBoardArchiveRequest { Archive = archive }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> CreateBoardAsync(CreateBoardRequest request) =>
        await http.Post<BoardResponse>(Endpoint("/api/v1/boards"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> UpdateBoardAsync(int boardId, UpdateBoardRequest request) =>
        await http.Put<BoardResponse>(Endpoint($"/api/v1/boards/{boardId}"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> DeleteBoardAsync(int boardId) =>
        await http.Http<AiurResponse>(Endpoint($"/api/v1/boards/{boardId}"),
            new AiurApiPayload(new { }), HttpMethod.Delete, BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> CreateColumnAsync(int boardId, CreateColumnRequest request) =>
        await http.Post<BoardResponse>(Endpoint($"/api/v1/boards/{boardId}/columns"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> UpdateColumnAsync(int columnId, UpdateColumnRequest request) =>
        await http.Put<BoardResponse>(Endpoint($"/api/v1/columns/{columnId}"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> MoveColumnAsync(int columnId, int newOrder) =>
        await http.Put<BoardResponse>(Endpoint($"/api/v1/columns/{columnId}/position"),
            new AiurApiPayload(new MoveColumnRequest { NewOrder = newOrder }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardResponse> DeleteColumnAsync(int columnId) =>
        await http.Http<BoardResponse>(Endpoint($"/api/v1/columns/{columnId}"),
            new AiurApiPayload(new { }), HttpMethod.Delete, BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardSharingResponse> GetBoardSharingAsync(int boardId) =>
        await http.Get<BoardSharingResponse>(Endpoint($"/api/v1/boards/{boardId}/sharing"),
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardSharingResponse> SetBoardVisibilityAsync(int boardId, bool isPublic) =>
        await http.Put<BoardSharingResponse>(Endpoint($"/api/v1/boards/{boardId}/visibility"),
            new AiurApiPayload(new UpdateBoardVisibilityRequest { IsPublic = isPublic }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardSharingResponse> AddBoardShareAsync(int boardId, AddBoardShareRequest request) =>
        await http.Post<BoardSharingResponse>(Endpoint($"/api/v1/boards/{boardId}/shares"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<BoardSharingResponse> RemoveBoardShareAsync(int boardId, Guid shareId) =>
        await http.Http<BoardSharingResponse>(Endpoint($"/api/v1/boards/{boardId}/shares/{shareId}"),
            new AiurApiPayload(new { }), HttpMethod.Delete, BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardResponse> CreateCardAsync(int columnId, CreateCardRequest request) =>
        await http.Post<CardResponse>(Endpoint($"/api/v1/columns/{columnId}/cards"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardResponse> MoveCardAsync(int cardId, MoveCardRequest request) =>
        await http.Put<CardResponse>(Endpoint($"/api/v1/cards/{cardId}/position"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardTransferTargetsResponse> GetCardTransferTargetsAsync(int cardId) =>
        await http.Get<CardTransferTargetsResponse>(Endpoint($"/api/v1/cards/{cardId}/transfer-targets"),
            headers: await AuthorizationHeadersAsync());

    public async Task<CardTransferResponse> TransferCardAsync(int cardId, TransferCardRequest request) =>
        await http.Post<CardTransferResponse>(Endpoint($"/api/v1/cards/{cardId}/transfer"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardDetailsResponse> GetCardDetailsAsync(int cardId) =>
        await http.Get<CardDetailsResponse>(Endpoint($"/api/v1/cards/{cardId}"), headers: await AuthorizationHeadersAsync());

    public async Task<CardDetailsResponse> UpdateCardAsync(int cardId, UpdateCardRequest request) =>
        await http.Put<CardDetailsResponse>(Endpoint($"/api/v1/cards/{cardId}"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> DeleteCardAsync(int cardId) =>
        await http.Http<AiurResponse>(Endpoint($"/api/v1/cards/{cardId}"), new AiurApiPayload(new { }), HttpMethod.Delete,
            BodyFormat.HttpJsonBody, headers: await AuthorizationHeadersAsync());

    public async Task<CardCommentResponse> AddCardCommentAsync(int cardId, AddCardCommentRequest request) =>
        await http.Post<CardCommentResponse>(Endpoint($"/api/v1/cards/{cardId}/comments"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<CardImageUploadGrantResponse> GetCardImageUploadGrantAsync() =>
        await http.Get<CardImageUploadGrantResponse>(Endpoint("/api/v1/uploads/card-images"),
            headers: await AuthorizationHeadersAsync());

    public async Task<CardImageUploadGrantResponse> GetAvatarUploadGrantAsync() =>
        await http.Get<CardImageUploadGrantResponse>(Endpoint("/api/v1/uploads/avatar"),
            headers: await AuthorizationHeadersAsync());

    public async Task<CardImageUploadResponse> UploadCardImageAsync(
        CardImageUploadGrantResponse grant,
        Stream imageStream,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (string.IsNullOrWhiteSpace(grant.UploadUrl))
        {
            throw new InvalidDataException("The server returned an empty image upload URL.");
        }

        var uploadUri = ResolveWebUri(grant.UploadUrl);
        using var multipart = new MultipartFormDataContent();
        var imageContent = new StreamContent(imageStream);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            imageContent.Headers.ContentType = mediaType;
        }
        multipart.Add(imageContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        request.Content = multipart;
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _rawHttp.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Image upload failed with HTTP {(int)response.StatusCode}: {responseBody}",
                inner: null,
                response.StatusCode);
        }

        var upload = JsonSerializer.Deserialize<CardImageUploadResponse>(responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (upload == null || string.IsNullOrWhiteSpace(upload.InternetPath))
        {
            throw new InvalidDataException("The server returned an invalid image upload response.");
        }

        return upload;
    }

    public async Task<byte[]> DownloadCardImageThumbnailAsync(
        string imageUrl,
        int width = 320,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 2048);

        var builder = new UriBuilder(ResolveWebUri(imageUrl));
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrWhiteSpace(query)
            ? $"w={width}"
            : $"{query}&w={width}";

        using var response = await _rawHttp.GetAsync(
            builder.Uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownloadedImageBytes)
        {
            throw new InvalidDataException("The image thumbnail is too large to display.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (destination.Length + read > MaxDownloadedImageBytes)
            {
                throw new InvalidDataException("The image thumbnail is too large to display.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    public async Task<AiurResponse> DeleteCardCommentAsync(int cardId, int commentId) =>
        await http.Http<AiurResponse>(Endpoint($"/api/v1/cards/{cardId}/comments/{commentId}"), new AiurApiPayload(new { }), HttpMethod.Delete,
            BodyFormat.HttpJsonBody, headers: await AuthorizationHeadersAsync());

    public async Task<CardLabelResponse> AddCardLabelAsync(int cardId, AddCardLabelRequest request) =>
        await http.Post<CardLabelResponse>(Endpoint($"/api/v1/cards/{cardId}/labels"), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> RemoveCardLabelAsync(int cardId, int labelId) =>
        await http.Http<AiurResponse>(Endpoint($"/api/v1/cards/{cardId}/labels/{labelId}"), new AiurApiPayload(new { }), HttpMethod.Delete,
            BodyFormat.HttpJsonBody, headers: await AuthorizationHeadersAsync());

    public async Task<CardSubscriptionResponse> SetCardSubscriptionAsync(int cardId, bool subscribe) =>
        await http.Put<CardSubscriptionResponse>(Endpoint($"/api/v1/cards/{cardId}/subscription"),
            new AiurApiPayload(new SetCardSubscriptionRequest { Subscribe = subscribe }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<DailyReportListResponse> GetDailyReportsAsync(int page = 1) =>
        await http.Get<DailyReportListResponse>(Endpoint("/api/v1/reports/daily", new { page }),
            headers: await AuthorizationHeadersAsync());

    public async Task<DailyReportResponse> GetDailyReportAsync(Guid reportId) =>
        await http.Get<DailyReportResponse>(Endpoint("/api/v1/reports/daily/{reportId}", new { reportId }),
            headers: await AuthorizationHeadersAsync());

    public async Task<DailyReportResponse> GenerateDailyReportAsync(string type) =>
        await http.Post<DailyReportResponse>(Endpoint($"/api/v1/reports/daily/{type}/generate"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<WeeklyReportListResponse> GetWeeklyReportsAsync(int page = 1) =>
        await http.Get<WeeklyReportListResponse>(Endpoint("/api/v1/reports/weekly", new { page }),
            headers: await AuthorizationHeadersAsync());

    public async Task<WeeklyReportResponse> GetWeeklyReportAsync(Guid reportId) =>
        await http.Get<WeeklyReportResponse>(Endpoint("/api/v1/reports/weekly/{reportId}", new { reportId }),
            headers: await AuthorizationHeadersAsync());

    public async Task<WeeklyReportResponse> GenerateWeeklyReportAsync() =>
        await http.Post<WeeklyReportResponse>(Endpoint("/api/v1/reports/weekly/generate"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> DeleteWeeklyReportAsync(Guid reportId) =>
        await http.Http<AiurResponse>(Endpoint("/api/v1/reports/weekly/{reportId}", new { reportId }),
            new AiurApiPayload(new { }), HttpMethod.Delete, BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<MyTasksResponse> GetMyTasksAsync(
        string? targetUserId = null,
        string status = "incomplete",
        IEnumerable<int>? labelIds = null,
        string labelMode = "any",
        string sort = "planned-end-desc") =>
        await http.Get<MyTasksResponse>(Endpoint("/api/v1/tasks/mine", new
        {
            targetUserId,
            status,
            labelIds = labelIds == null ? null : string.Join(',', labelIds),
            labelMode,
            sort
        }), headers: await AuthorizationHeadersAsync());

    public async Task<CardSearchResponse> SearchCardsAsync(string query) =>
        await http.Get<CardSearchResponse>(Endpoint("/api/v1/search/cards", new { query }),
            headers: await AuthorizationHeadersAsync());

    public async Task<DashboardResponse> GetDashboardAsync() =>
        await http.Get<DashboardResponse>(Endpoint("/api/v1/dashboard"),
            headers: await AuthorizationHeadersAsync());

    public async Task<AccountProfileResponse> GetAccountProfileAsync() =>
        await http.Get<AccountProfileResponse>(Endpoint("/api/v1/account"),
            headers: await AuthorizationHeadersAsync());

    public async Task<AccountProfileResponse> UpdateProfileAsync(UpdateProfileRequest request) =>
        await http.Put<AccountProfileResponse>(Endpoint("/api/v1/account/profile"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> ChangePasswordAsync(ChangePasswordRequest request) =>
        await http.Put<AiurResponse>(Endpoint("/api/v1/account/password"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AccountProfileResponse> UpdateReportSettingsAsync(UpdateReportSettingsRequest request) =>
        await http.Put<AccountProfileResponse>(Endpoint("/api/v1/account/report-settings"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AccountProfileResponse> UpdateAvatarAsync(string avatarRelativePath) =>
        await http.Put<AccountProfileResponse>(Endpoint("/api/v1/account/avatar"),
            new AiurApiPayload(new UpdateAvatarRequest { AvatarRelativePath = avatarRelativePath }),
            BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> DeleteAccountAsync() =>
        await http.Http<AiurResponse>(Endpoint("/api/v1/account"),
            new AiurApiPayload(new { }), HttpMethod.Delete, BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<NotificationListResponse> GetNotificationsAsync() =>
        await http.Get<NotificationListResponse>(Endpoint("/api/v1/notifications"),
            headers: await AuthorizationHeadersAsync());

    public async Task<OperationLogListResponse> GetMyOperationLogsAsync(int page = 1) =>
        await http.Get<OperationLogListResponse>(Endpoint("/api/v1/audit-logs/mine", new { page }),
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> MarkNotificationReadAsync(int notificationId) =>
        await http.Put<AiurResponse>(Endpoint("/api/v1/notifications/{notificationId}/read", new { notificationId }),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> MarkAllNotificationsReadAsync() =>
        await http.Put<AiurResponse>(Endpoint("/api/v1/notifications/read-all"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AgentConversationResponse> SendAgentMessageAsync(AgentSendMessageRequest request) =>
        await http.Post<AgentConversationResponse>(Endpoint("/api/v1/agent/messages"),
            new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AgentStatusResponse> GetAgentStatusAsync(Guid conversationId) =>
        await http.Get<AgentStatusResponse>(Endpoint($"/api/v1/agent/conversations/{conversationId}"),
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> ApproveAgentAdviceAsync(Guid conversationId, Guid adviceId) =>
        await http.Post<AiurResponse>(
            Endpoint($"/api/v1/agent/conversations/{conversationId}/advice/{adviceId}/approve"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> RejectAgentAdviceAsync(Guid conversationId, Guid adviceId) =>
        await http.Post<AiurResponse>(
            Endpoint($"/api/v1/agent/conversations/{conversationId}/advice/{adviceId}/reject"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> ApproveAllAgentAdviceAsync(Guid conversationId) =>
        await http.Post<AiurResponse>(Endpoint($"/api/v1/agent/conversations/{conversationId}/approve-all"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AiurResponse> CancelAgentConversationAsync(Guid conversationId) =>
        await http.Post<AiurResponse>(Endpoint($"/api/v1/agent/conversations/{conversationId}/cancel"),
            new AiurApiPayload(new { }), BodyFormat.HttpJsonBody,
            headers: await AuthorizationHeadersAsync());

    public async Task<AgentExcelConversionResponse> ConvertAgentExcelAsync(
        Stream workbookStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbookStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var multipart = new MultipartFormDataContent();
        var workbookContent = new StreamContent(workbookStream);
        workbookContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        multipart.Add(workbookContent, "file", Path.GetFileName(fileName));
        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveWebUri("/api/v1/agent/excel"));
        request.Content = multipart;
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _rawHttp.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<AiurResponse>(responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            throw new HttpRequestException(
                error?.Message ?? $"Excel conversion failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<AgentExcelConversionResponse>(responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (result == null || (int)result.Code < 0 || string.IsNullOrWhiteSpace(result.Markdown))
        {
            throw new InvalidDataException(result?.Message ?? "The server returned an invalid Excel conversion response.");
        }
        return result;
    }

    private AiurApiEndpoint Endpoint(string route) => new(_endpoint, route, new { });

    private AiurApiEndpoint Endpoint(string route, object parameters) => new(_endpoint, route, parameters);

    private Uri ResolveWebUri(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri;
        }
        if (value.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Only HTTP and HTTPS URLs are supported.");
        }
        return new Uri(new Uri($"{_endpoint}/"), value);
    }

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

    public void Dispose() => _rawHttp.Dispose();
}
