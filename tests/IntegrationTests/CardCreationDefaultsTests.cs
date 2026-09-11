using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class CardCreationDefaultsTests : TestBase
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 11, 15, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ApplyAsyncUsesEnabledDefaults()
    {
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var service = new CardCreationDefaultsService(settings, new FixedTimeProvider(FixedNow));
        var card = new KanbanCard { Title = "Default dates" };

        await service.ApplyAsync(card);

        Assert.AreEqual(new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), card.DueDate);
        Assert.AreEqual(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), card.PlannedStartTime);
    }

    [TestMethod]
    public async Task ApplyAsyncRespectsDisabledSettings()
    {
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        try
        {
            await settings.UpdateSettingAsync(SettingsMap.AutoSetDueDate, "False");
            await settings.UpdateSettingAsync(SettingsMap.AutoSetPlannedStartTime, "False");
            var service = new CardCreationDefaultsService(settings, new FixedTimeProvider(FixedNow));
            var card = new KanbanCard { Title = "No default dates" };

            await service.ApplyAsync(card);

            Assert.IsNull(card.DueDate);
            Assert.IsNull(card.PlannedStartTime);
        }
        finally
        {
            await settings.UpdateSettingAsync(SettingsMap.AutoSetDueDate, "True");
            await settings.UpdateSettingAsync(SettingsMap.AutoSetPlannedStartTime, "True");
        }
    }

    [TestMethod]
    public async Task ApplyAsyncPreservesExplicitDates()
    {
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var service = new CardCreationDefaultsService(settings, new FixedTimeProvider(FixedNow));
        var explicitDueDate = new DateTime(2027, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var explicitPlannedStart = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var card = new KanbanCard
        {
            Title = "Explicit dates",
            DueDate = explicitDueDate,
            PlannedStartTime = explicitPlannedStart
        };

        await service.ApplyAsync(card);

        Assert.AreEqual(explicitDueDate, card.DueDate);
        Assert.AreEqual(explicitPlannedStart, card.PlannedStartTime);
    }

    [TestMethod]
    public async Task ApplyAsyncUsesConfiguredAdvanceDays()
    {
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        try
        {
            await settings.UpdateSettingAsync(SettingsMap.DueDateAdvanceDays, "21");
            await settings.UpdateSettingAsync(SettingsMap.PlannedStartTimeAdvanceDays, "5");
            var service = new CardCreationDefaultsService(settings, new FixedTimeProvider(FixedNow));
            var card = new KanbanCard { Title = "Configured dates" };

            await service.ApplyAsync(card);

            Assert.AreEqual(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), card.DueDate);
            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), card.PlannedStartTime);
        }
        finally
        {
            await settings.UpdateSettingAsync(SettingsMap.DueDateAdvanceDays, "14");
            await settings.UpdateSettingAsync(SettingsMap.PlannedStartTimeAdvanceDays, "4");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
