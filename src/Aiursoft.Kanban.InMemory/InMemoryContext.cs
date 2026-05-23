using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.InMemory;

public class InMemoryContext(DbContextOptions<InMemoryContext> options) : TemplateDbContext(options)
{
    public override Task MigrateAsync(CancellationToken cancellationToken)
    {
        return Database.EnsureCreatedAsync(cancellationToken);
    }

    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
