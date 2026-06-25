using Aiursoft.ClickhouseSdk;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogQueryService(
    AuditClickhouseDbContext clickhouse,
    IOptionsMonitor<ClickhouseOptions> options) : IScopedDependency
{
    public bool Enabled => options.CurrentValue.Enabled;

    public async Task<(IReadOnlyList<AuditLog> Logs, int Total)> GetLogsAsync(
        string? userId,
        int page,
        int pageSize)
    {
        if (!Enabled) return ([], 0);

        var tableName = ClickhouseIdentifier.Quote(options.CurrentValue.TableName);
        var where = userId == null ? string.Empty : " WHERE UserId = {userId:String}";
        var parameters = userId == null
            ? null
            : new Dictionary<string, object?>
            {
                ["userId"] = userId
            };
        var total = Convert.ToInt32(await clickhouse.ExecuteScalarAsync<ulong>(
            $"SELECT count() FROM {tableName}{where}",
            parameters));

        var logs = await clickhouse.QueryAsync(
            $"""
            SELECT EventTime, UserId, UserName, Action, Category, Summary, Details, Source, IpAddress, TraceId
            FROM {tableName}{where}
            ORDER BY EventTime DESC
            LIMIT {pageSize} OFFSET {(page - 1) * pageSize}
            """,
            reader => new AuditLog
            {
                EventTime = reader.GetDateTime(0),
                UserId = reader.GetString(1),
                UserName = reader.GetString(2),
                Action = reader.GetString(3),
                Category = reader.GetString(4),
                Summary = reader.GetString(5),
                Details = reader.GetString(6),
                Source = reader.GetString(7),
                IpAddress = reader.GetString(8),
                TraceId = reader.GetString(9)
            },
            parameters);

        return (logs, total);
    }
}
