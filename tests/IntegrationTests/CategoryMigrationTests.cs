using Aiursoft.Kanban.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class CategoryMigrationTests
{
    [TestMethod]
    public async Task ExistingBoardsReceiveDefaultAndCardsKeepTheirOriginalStatusAndDates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrations = context.Database.GetMigrations().ToArray();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO KanbanBoards (Id, Name, IsPublic, CreationTime, IsArchived, "Order")
            VALUES (1, 'Existing board', 0, '2026-11-01', 0, 0), (2, 'Empty archived board', 0, '2026-11-01', 1, 1);
            INSERT INTO KanbanColumns (Id, Name, "Order", BoardId, CreationTime, ColumnStatus)
            VALUES (1, 'Manufacturing', 0, 1, '2026-11-01', 1);
            INSERT INTO KanbanCards (Id, Title, "Order", ColumnId, CreationTime, Priority, RecurrenceUnit, LastUpdatedAt, LastEmbeddedAt, ActualStartTime)
            VALUES (1, 'Pump', 7, 1, '2026-11-01', 4, 0, '2026-11-01', '2026-11-01', '2026-11-02');
            """);
        await migrator.MigrateAsync();
        var categories = await context.KanbanCategories.OrderBy(c => c.BoardId).ToListAsync();
        Assert.HasCount(2, categories);
        Assert.IsTrue(categories.All(c => c.IsDefault && c.Name == "Default"));
        var card = await context.KanbanCards.Include(c => c.Column).SingleAsync();
        Assert.AreEqual(categories[0].Id, card.CategoryId);
        Assert.AreEqual(7, card.Order);
        Assert.AreEqual(7, card.CategoryOrder);
        Assert.AreEqual("Manufacturing", card.Column.Name);
        Assert.AreEqual(Entities.ColumnStatus.InProgress, card.Column.ColumnStatus);
        Assert.AreEqual(new DateTime(2026, 11, 2), card.ActualStartTime);
        Assert.IsNull(card.ActualEndTime);
        await migrator.MigrateAsync(migrations[^2]);
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.AreEqual(1, await context.KanbanCards.CountAsync());
        Assert.AreEqual(2, await context.KanbanCategories.CountAsync());
    }
}
