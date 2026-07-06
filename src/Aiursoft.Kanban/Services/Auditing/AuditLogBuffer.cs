using System.Threading.Channels;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogBuffer(ILogger<AuditLogBuffer> logger) : ISingletonDependency
{
    private readonly Channel<AuditLog> _channel = Channel.CreateBounded<AuditLog>(
        new BoundedChannelOptions(10000) { FullMode = BoundedChannelFullMode.Wait });

    public async Task<bool> EnqueueAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await _channel.Writer.WriteAsync(auditLog, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Timed out while writing audit log action {Action} for user {UserId} to the buffer",
                auditLog.Action, auditLog.UserId);
            return false;
        }
    }

    public bool TryEnqueue(AuditLog auditLog)
    {
        if (_channel.Writer.TryWrite(auditLog))
        {
            return true;
        }

        logger.LogError("Audit log buffer is full; dropped audit log action {Action} for user {UserId}",
            auditLog.Action, auditLog.UserId);
        return false;
    }

    public int Drain(List<AuditLog> batch)
    {
        while (_channel.Reader.TryRead(out var auditLog))
        {
            batch.Add(auditLog);
        }

        return batch.Count;
    }
}
