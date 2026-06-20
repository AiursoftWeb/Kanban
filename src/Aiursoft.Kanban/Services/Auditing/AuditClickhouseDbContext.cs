using Aiursoft.ClickhouseSdk;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditClickhouseDbContext : ClickhouseDbContext, IScopedDependency
{
    public ClickhouseSet<AuditLog> AuditLogs { get; }

    public AuditClickhouseDbContext(IOptionsMonitor<ClickhouseOptions> options) : base(options)
    {
        AuditLogs = new ClickhouseSet<AuditLog>(GetConnection, options.CurrentValue.TableName, log =>
        [
            log.EventTime,
            log.UserId,
            log.UserName,
            log.Action,
            log.Category,
            log.Summary,
            log.Details,
            log.Source,
            log.IpAddress,
            log.TraceId
        ]);
    }

    public override async Task SaveChangesAsync()
    {
        await AuditLogs.SaveChangesAsync();
    }
}
