using System.Net;
using System.Text;
using Aiursoft.AiurProtocol.Exceptions;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;

namespace Aiursoft.Kanban.Tests.Sdk;

[TestClass]
public sealed class KanbanApiClientTests
{
    [TestMethod]
    public async Task GetBoardUsesConfiguredEndpointAndBearerToken()
    {
        var handler = new RecordingHandler("""
            {"code":0,"message":"ok","protocolVersion":"10.0.30","board":{"id":42,"name":"Mobile","columns":[]}}
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetBoardAsync(42);

        Assert.AreEqual(42, result.Board.Id);
        Assert.AreEqual("https://kanban.example/api/v1/boards/42", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("access-token", handler.AuthorizationParameter);
    }

    [TestMethod]
    public async Task CreateCardPostsJsonThroughAiurProtocol()
    {
        var handler = new RecordingHandler("""
            {"code":2,"message":"created","protocolVersion":"10.0.30","card":{"id":9,"columnId":3,"title":"Ship Android","order":0}}
            """);
        await using var provider = BuildProvider(handler, "token");

        var result = await provider.GetRequiredService<KanbanApiClient>().CreateCardAsync(3, new CreateCardRequest
        {
            Title = "Ship Android",
            Description = "Native .NET"
        });

        Assert.AreEqual(9, result.Card.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("application/json", handler.ContentType);
        StringAssert.Contains(handler.Body ?? string.Empty, "Ship Android");
    }

    [TestMethod]
    public async Task InvalidRequestIsRejectedBeforeNetworkCall()
    {
        var handler = new RecordingHandler("{}");
        await using var provider = BuildProvider(handler, "token");

        await Assert.ThrowsAsync<AiurBadApiInputException>(() =>
            provider.GetRequiredService<KanbanApiClient>().CreateCardAsync(1, new CreateCardRequest()));
        Assert.AreEqual(0, handler.CallCount);
    }

    private static ServiceProvider BuildProvider(RecordingHandler handler, string? token)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKanbanSdk("https://kanban.example/");
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
        services.AddScoped<IKanbanAccessTokenProvider>(_ => new StubTokenProvider(token));
        return services.BuildServiceProvider();
    }

    private sealed class StubTokenProvider(string? token) : IKanbanAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(token);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
