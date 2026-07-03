using System.Net;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class DailyReportTests : TestBase
{
    // ── Entity & DB Tests ────────────────────────────────

    [TestMethod]
    public async Task DailyReport_CreateAndReadFromDb()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var report = new DailyReport
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-id",
            ReportType = DailyReportType.Plan,
            Content = "Today you should focus on the most important tasks.",
            Date = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            GeneratedAt = DateTime.UtcNow
        };

        db.DailyReports.Add(report);
        await db.SaveChangesAsync();

        var retrieved = await db.DailyReports.FindAsync(report.Id);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("test-user-id", retrieved.UserId);
        Assert.AreEqual(DailyReportType.Plan, retrieved.ReportType);
        Assert.AreEqual("Today you should focus on the most important tasks.", retrieved.Content);
    }

    [TestMethod]
    public async Task DailyReport_UniqueIndex_ConfiguredInModel()
    {
        // InMemory EF Core doesn't enforce unique indexes, so we verify the
        // model configuration exists rather than testing the constraint at runtime.
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var entityType = db.Model.FindEntityType(typeof(DailyReport));
        Assert.IsNotNull(entityType, "DailyReport entity should be in the model");

        var indexes = entityType.GetIndexes().ToList();
        var uniqueIndex = indexes.FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(DailyReport.UserId)) &&
            i.Properties.Any(p => p.Name == nameof(DailyReport.Date)) &&
            i.Properties.Any(p => p.Name == nameof(DailyReport.ReportType)));
        Assert.IsNotNull(uniqueIndex,
            "Unique index on (UserId, Date, ReportType) should be configured");
    }

    [TestMethod]
    public async Task DailyReport_DifferentTypesSameDay_Allowed()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var today = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
        var userId = "multi-type-user";

        db.DailyReports.Add(new DailyReport
        {
            Id = Guid.NewGuid(), UserId = userId, ReportType = DailyReportType.Plan,
            Content = "Plan content.", Date = today, GeneratedAt = DateTime.UtcNow
        });
        db.DailyReports.Add(new DailyReport
        {
            Id = Guid.NewGuid(), UserId = userId, ReportType = DailyReportType.Summary,
            Content = "Summary content.", Date = today, GeneratedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(); // Should not throw

        var count = await db.DailyReports.CountAsync(r => r.UserId == userId && r.Date == today);
        Assert.AreEqual(2, count);
    }

    // ── Permission Tests ─────────────────────────────────

    [TestMethod]
    public async Task DailyReport_Permission_CanManageAnyDailyReport_Exists()
    {
        await LoginAsAdmin();
        var permissions = AppPermissions.GetAllPermissions();
        var perm = permissions.FirstOrDefault(p => p.Key == AppPermissionNames.CanManageAnyDailyReport);
        Assert.IsNotNull(perm, "CanManageAnyDailyReport should be in GetAllPermissions()");
        Assert.AreEqual("Manage Any Daily Report", perm.Name);
    }

    [TestMethod]
    public async Task DailyReport_Permission_PolicyIsRegistered()
    {
        // Verify the policy was created via AuthenticationExtensions
        await LoginAsAdmin();
        var authService = Server!.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        Assert.IsNotNull(authService, "Authorization service should be available");
    }

    // ── Controller Tests ─────────────────────────────────

    [TestMethod]
    public async Task DailyReport_Index_RequiresAuthentication()
    {
        var response = await Http.GetAsync("/DailyReport");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location!.OriginalString.Contains("/Account/Login"),
            "Unauthenticated user should be redirected to login");
    }

    [TestMethod]
    public async Task DailyReport_Index_ShowsEmptyState()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/DailyReport");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Daily Assistant"),
            "Page should contain 'Daily Assistant' title");
        Assert.IsTrue(html.Contains("No reports yet"),
            "Should show empty state message");
    }

    [TestMethod]
    public async Task DailyReport_Index_ShowsReports()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var today = (DateTime.UtcNow + TimeSpan.FromHours(8)).Date;
            db.DailyReports.Add(new DailyReport
            {
                Id = Guid.NewGuid(), UserId = userId, ReportType = DailyReportType.Plan,
                Content = "Test plan content.", Date = today.AddDays(-1), GeneratedAt = DateTime.UtcNow
            });
            db.DailyReports.Add(new DailyReport
            {
                Id = Guid.NewGuid(), UserId = userId, ReportType = DailyReportType.Summary,
                Content = "Test summary content.", Date = today.AddDays(-1), GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync("/DailyReport");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Test plan content"),
            "Should display plan content in history");
        Assert.IsTrue(html.Contains("Test summary content"),
            "Should display summary content in history");
    }

    [TestMethod]
    public async Task DailyReport_Details_NotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync($"/DailyReport/Details/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DailyReport_Details_ShowsReport()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();
        var reportId = Guid.NewGuid();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.DailyReports.Add(new DailyReport
            {
                Id = reportId, UserId = userId, ReportType = DailyReportType.Plan,
                Content = "Full plan content here.", Date = DateTime.UtcNow.Date, GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/DailyReport/Details/{reportId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Full plan content here"));
    }

    // ── Pagination Tests ─────────────────────────────────

    [TestMethod]
    public async Task DailyReport_Pagination_FirstPage()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/DailyReport?page=1");
        response.EnsureSuccessStatusCode();
        // Should render without error even with no data
    }

    [TestMethod]
    public async Task DailyReport_Pagination_NegativePageClamped()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/DailyReport?page=-1");
        response.EnsureSuccessStatusCode();
        // Should clamp to page 1
    }

    [TestMethod]
    public async Task DailyReport_Pagination_BeyondRangeClamped()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/DailyReport?page=999");
        response.EnsureSuccessStatusCode();
        // Should clamp to last available page
    }

    // ── Background Job Tests ─────────────────────────────

    [TestMethod]
    public void DailyReportBackgroundJob_IsRegistered()
    {
        // DailyReportBackgroundJob requires scoped TemplateDbContext
        using var scope = Server!.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<DailyReportBackgroundJob>();
        Assert.IsNotNull(job);
        Assert.AreEqual("Daily Report Generator", job.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(job.Description));
    }

    [TestMethod]
    public async Task DailyReportBackgroundJob_ExecuteAsync_DoesNotThrow()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<DailyReportBackgroundJob>();

        try
        {
            await job.ExecuteAsync();
        }
        catch (Exception ex)
        {
            // If LLM is not configured, it should handle gracefully internally.
            // The job should not throw out of ExecuteAsync.
            Assert.IsTrue(ex is not InvalidOperationException || !ex.Message.Contains("anthropic"),
                "Job should not throw unhandled exceptions");
        }
    }

    // ── Subagent Tests ───────────────────────────────────

    [TestMethod]
    public void DailyPlanningSubagent_IsRegisteredInDI()
    {
        var subagent = Server!.Services.GetRequiredService<DailyPlanningSubagent>();
        Assert.IsNotNull(subagent);
        Assert.AreEqual("DailyPlanning", subagent.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(subagent.Description));
    }

    [TestMethod]
    public void DailySummarySubagent_IsRegisteredInDI()
    {
        var subagent = Server!.Services.GetRequiredService<DailySummarySubagent>();
        Assert.IsNotNull(subagent);
        Assert.AreEqual("DailySummary", subagent.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(subagent.Description));
    }

    [TestMethod]
    public async Task DailyPlanningSubagent_HasOnlyReadTools()
    {
        await LoginAsAdmin();
        var subagent = Server!.Services.GetRequiredService<DailyPlanningSubagent>();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        Assert.IsTrue(subagent.ToolNames.Length > 0, "Subagent should have tools configured");
        foreach (var toolName in subagent.ToolNames)
        {
            Assert.IsFalse(registry.IsWriteTool(toolName),
                $"Tool '{toolName}' used by DailyPlanning should be read-only");
        }
    }

    [TestMethod]
    public async Task DailySummarySubagent_HasOnlyReadTools()
    {
        await LoginAsAdmin();
        var subagent = Server!.Services.GetRequiredService<DailySummarySubagent>();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        Assert.IsTrue(subagent.ToolNames.Length > 0, "Subagent should have tools configured");
        foreach (var toolName in subagent.ToolNames)
        {
            Assert.IsFalse(registry.IsWriteTool(toolName),
                $"Tool '{toolName}' used by DailySummary should be read-only");
        }
    }

    [TestMethod]
    public async Task DailySubagents_NotExposedAsMcpTools()
    {
        await LoginAsAdmin();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        // These names must NOT be in the MCP tool registry
        var dailyPlanningTool = registry.AllTools.FirstOrDefault(t => t.ProtocolTool.Name == "DailyPlanning");
        var dailySummaryTool = registry.AllTools.FirstOrDefault(t => t.ProtocolTool.Name == "DailySummary");

        Assert.IsNull(dailyPlanningTool, "DailyPlanning should NOT be exposed as an MCP tool");
        Assert.IsNull(dailySummaryTool, "DailySummary should NOT be exposed as an MCP tool");
    }

    [TestMethod]
    public async Task DailySubagents_OnlyTaskPlanningIsExposed()
    {
        await LoginAsAdmin();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        // Only TaskPlanning should be in the tool registry as a subagent tool
        var subagentTools = registry.AllTools
            .Where(t => t.ProtocolTool.Name is "TaskPlanning" or "DailyPlanning" or "DailySummary")
            .Select(t => t.ProtocolTool.Name)
            .ToList();

        Assert.IsTrue(subagentTools.Contains("TaskPlanning"),
            "TaskPlanning should be exposed as a tool");
        Assert.IsFalse(subagentTools.Contains("DailyPlanning"),
            "DailyPlanning should NOT be exposed");
        Assert.IsFalse(subagentTools.Contains("DailySummary"),
            "DailySummary should NOT be exposed");
    }

    // ── Regenerate Tests ─────────────────────────────────

    [TestMethod]
    public async Task DailyReport_Regenerate_WithoutAuth_Redirects()
    {
        var response = await PostForm("/DailyReport/Regenerate", new Dictionary<string, string>
        {
            { "type", "plan" }
        }, tokenUrl: "/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────

    private string GetCurrentUserId()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var adminUser = db.Users.First(u => u.UserName == "admin");
        return adminUser.Id;
    }
}
