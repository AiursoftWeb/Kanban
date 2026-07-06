namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogShutdownFlushService(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditLogShutdownFlushService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var flushExecutor = scope.ServiceProvider.GetRequiredService<AuditLogFlushExecutor>();
            await flushExecutor.FlushAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush audit logs during application shutdown");
        }
    }
}
