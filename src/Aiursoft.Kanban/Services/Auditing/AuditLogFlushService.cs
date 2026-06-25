using Aiursoft.Canon.BackgroundJobs;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogFlushService(
    AuditLogBuffer buffer,
    AuditClickhouseDbContext clickhouse,
    ILogger<AuditLogFlushService> logger) : IBackgroundJob
{
    public string Name => "ClickHouse Audit Log Flush";
    public string Description => "Writes buffered user operation logs to ClickHouse.";

    public async Task ExecuteAsync()
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
            foreach (var auditLog in batch)
            {
                await buffer.EnqueueAsync(auditLog);
            }
        }
    }
}
