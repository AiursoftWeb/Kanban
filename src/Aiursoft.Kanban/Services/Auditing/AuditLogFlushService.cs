using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogFlushExecutor(
    AuditLogBuffer buffer,
    AuditClickhouseDbContext clickhouse,
    ILogger<AuditLogFlushExecutor> logger) : IScopedDependency
{
    public async Task FlushAsync()
    {
        if (!clickhouse.Enabled) return;

        var batch = new List<Entities.AuditLog>();
        if (buffer.Drain(batch) == 0) return;

        foreach (var auditLog in batch)
        {
            clickhouse.AuditLogs.Add(auditLog);
        }

        try
        {
            await clickhouse.SaveChangesAsync();
            logger.LogInformation("Flushed {Count} audit logs to ClickHouse", batch.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush {Count} audit logs to ClickHouse; requeueing the batch", batch.Count);
            var dropped = 0;
            foreach (var auditLog in batch)
            {
                if (!buffer.TryEnqueue(auditLog))
                {
                    dropped++;
                }
            }

            if (dropped > 0)
            {
                logger.LogError("Dropped {Count} audit logs after ClickHouse flush failure because the buffer was full",
                    dropped);
            }
        }
    }
}

public class AuditLogFlushService(AuditLogFlushExecutor flushExecutor) : IBackgroundJob
{
    public string Name => "ClickHouse Audit Log Flush";
    public string Description => "Writes buffered user operation logs to ClickHouse.";

    public Task ExecuteAsync()
    {
        return flushExecutor.FlushAsync();
    }
}
