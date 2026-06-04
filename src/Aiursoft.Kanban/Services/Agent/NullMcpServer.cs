using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Agent;

#pragma warning disable MCPEXP002
internal sealed class NullMcpServer : McpServer
{
    public static readonly NullMcpServer Instance = new();

    public override ClientCapabilities? ClientCapabilities => null;
    public override Implementation? ClientInfo => null;
    public override McpServerOptions ServerOptions => new() { ServerInfo = new Implementation { Name = "KanbanAgent", Version = "1.0" } };
    public override IServiceProvider? Services => null;
    public override LoggingLevel? LoggingLevel => null;
    public override string? NegotiatedProtocolVersion => null;
    public override string? SessionId => null;

    public override Task RunAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new JsonRpcResponse { Id = request.Id, Result = null });

    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        => new NullAsyncDisposable();

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class NullAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
