using System.Diagnostics.CodeAnalysis;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<KanbanCard>()
            .Property(card => card.Description)
            .HasColumnType("mediumtext");
    }
}
