using System.Threading.Channels;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogBuffer(ILogger<AuditLogBuffer> logger) : ISingletonDependency
{
    private readonly Channel<AuditLog> _channel = Channel.CreateBounded<AuditLog>(
        new BoundedChannelOptions(10000) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Enqueue(AuditLog auditLog)
    {
        if (!_channel.Writer.TryWrite(auditLog))
        {
            logger.LogWarning("Audit log buffer is full; dropping action {Action} for user {UserId}",
                auditLog.Action, auditLog.UserId);
        }
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
