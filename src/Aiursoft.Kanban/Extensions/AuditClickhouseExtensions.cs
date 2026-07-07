using Aiursoft.ClickhouseSdk;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Entities;
using Microsoft.Extensions.Options;

namespace Aiursoft.Kanban.Extensions;

public static class AuditClickhouseExtensions
{
    public static async Task InitAuditClickhouseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<ClickhouseOptions>>();
        if (!options.CurrentValue.Enabled) return;

        await host.Services.InitClickhouseTableAsync<AuditLog>(options.CurrentValue.TableName, "EventTime");
    }
}
