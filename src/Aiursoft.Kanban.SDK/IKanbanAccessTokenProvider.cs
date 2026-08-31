namespace Aiursoft.Kanban.SDK;

public interface IKanbanAccessTokenProvider
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class EmptyKanbanAccessTokenProvider : IKanbanAccessTokenProvider
{
    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}
