using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Entities;
using Aiursoft.Scanner.Abstractions;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogQueryService(IOptionsMonitor<ClickhouseOptions> options) : IScopedDependency
{
    public bool Enabled => options.CurrentValue.Enabled;

    public async Task<(IReadOnlyList<AuditLog> Logs, int Total)> GetLogsAsync(
        string? userId,
        int page,
        int pageSize)
    {
        if (!Enabled) return ([], 0);

        var tableName = options.CurrentValue.TableName;
        await using var connection = new ClickHouseConnection(options.CurrentValue.ConnectionString);
        await connection.OpenAsync();

        var where = userId == null ? string.Empty : " WHERE UserId = {userId:String}";
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT count() FROM {tableName}{where}";
        if (userId != null) countCommand.AddParameter("userId", userId);
        var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT EventTime, UserId, UserName, Action, Category, Summary, Details, Source, IpAddress, TraceId
            FROM {tableName}{where}
            ORDER BY EventTime DESC
            LIMIT {pageSize} OFFSET {(page - 1) * pageSize}
            """;
        if (userId != null) command.AddParameter("userId", userId);

        var logs = new List<AuditLog>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new AuditLog
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
            });
        }

        return (logs, total);
    }
}
