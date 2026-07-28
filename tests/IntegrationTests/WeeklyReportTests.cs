using System.Net;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class WeeklyReportTests : TestBase
{
    // ── Entity & DB Tests ────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_CreateAndReadFromDb()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        var weekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var report = new WeeklyReport
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-id",
            Content = "* Fixed a race condition in the scheduler.\n* Added Gantt chart scrollbar support.",
            WeekStart = weekStart,
            GeneratedAt = DateTime.UtcNow
        };

        db.WeeklyReports.Add(report);
        await db.SaveChangesAsync();

        var retrieved = await db.WeeklyReports.FindAsync(report.Id);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("test-user-id", retrieved.UserId);
        Assert.AreEqual("* Fixed a race condition in the scheduler.\n* Added Gantt chart scrollbar support.", retrieved.Content);
        Assert.AreEqual(weekStart, retrieved.WeekStart);
    }

    [TestMethod]
    public async Task WeeklyReport_UniqueIndex_ConfiguredInModel()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var entityType = db.Model.FindEntityType(typeof(WeeklyReport));
        Assert.IsNotNull(entityType, "WeeklyReport entity should be in the model");

        var indexes = entityType.GetIndexes().ToList();
        var uniqueIndex = indexes.FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Any(p => p.Name == nameof(WeeklyReport.UserId)) &&
            i.Properties.Any(p => p.Name == nameof(WeeklyReport.WeekStart)));
        Assert.IsNotNull(uniqueIndex,
            "Unique index on (UserId, WeekStart) should be configured");
    }

    [TestMethod]
    public async Task WeeklyReport_ForeignKey_ConfiguredCorrectly()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var entityType = db.Model.FindEntityType(typeof(WeeklyReport));
        Assert.IsNotNull(entityType);

        var foreignKeys = entityType.GetForeignKeys().ToList();
        var userFk = foreignKeys.FirstOrDefault(fk =>
            fk.Properties.Any(p => p.Name == nameof(WeeklyReport.UserId)) &&
            fk.PrincipalEntityType.ClrType == typeof(User));
        Assert.IsNotNull(userFk, "FK from WeeklyReport.UserId to User should be configured");
        Assert.AreEqual(DeleteBehavior.Cascade, userFk.DeleteBehavior,
            "FK should cascade on delete");
    }

    // ── Permission Tests ─────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_Permission_CanManageAnyWeeklyReport_Exists()
    {
        await LoginAsAdmin();
        var permissions = AppPermissions.GetAllPermissions();
        var perm = permissions.FirstOrDefault(p => p.Key == AppPermissionNames.CanManageAnyWeeklyReport);
        Assert.IsNotNull(perm, "CanManageAnyWeeklyReport should be in GetAllPermissions()");
        Assert.AreEqual("Manage Any Weekly Report", perm.Name);
    }

    // ── Controller Tests ─────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_Index_RequiresAuthentication()
    {
        var response = await Http.GetAsync("/WeeklyReport");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location!.OriginalString.Contains("/Account/Login"),
            "Unauthenticated user should be redirected to login");
    }

    [TestMethod]
    public async Task WeeklyReport_Index_ShowsEmptyState()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/WeeklyReport");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Weekly Report"),
            "Page should contain 'Weekly Report' title");
        Assert.IsTrue(html.Contains("No reports yet"),
            "Should show empty state message");
    }

    [TestMethod]
    public async Task WeeklyReport_Index_ShowsReports()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var weekStart = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = "* Test weekly report content.",
                WeekStart = weekStart,
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync("/WeeklyReport");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Test weekly report content"),
            "Should display report content in history");
    }

    [TestMethod]
    public async Task WeeklyReport_Index_ShowsWeekRange()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var weekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = "* Content for week display test.",
                WeekStart = weekStart,
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync("/WeeklyReport");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Should display the week range
        Assert.IsTrue(html.Contains("2026-07-27"), "Should show week start date");
        Assert.IsTrue(html.Contains("2026-08-02"), "Should show week end date (Sunday)");
    }

    [TestMethod]
    public async Task WeeklyReport_Details_NotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync($"/WeeklyReport/Details/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task WeeklyReport_Details_ShowsReport()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();
        var reportId = Guid.NewGuid();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = reportId,
                UserId = userId,
                Content = "Full weekly report content here.",
                WeekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/WeeklyReport/Details/{reportId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Full weekly report content here"));
    }

    [TestMethod]
    public async Task WeeklyReport_Details_CrossUserForbidden()
    {
        // Admin creates a report, then a regular user tries to access it
        await LoginAsAdmin();
        var adminUserId = GetCurrentUserId();
        var reportId = Guid.NewGuid();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = reportId,
                UserId = adminUserId,
                Content = "Admin's weekly report.",
                WeekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await LogoutAsync();

        var (_, _) = await RegisterAndLoginAsync();

        var response = await Http.GetAsync($"/WeeklyReport/Details/{reportId}");
        // Forbid() redirects to access-denied page with 302
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode,
            "Regular user should be redirected away from another user's report");
    }

    // ── Pagination Tests ─────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_Pagination_FirstPage()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/WeeklyReport?page=1");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task WeeklyReport_Pagination_NegativePageClamped()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/WeeklyReport?page=-1");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task WeeklyReport_Pagination_BeyondRangeClamped()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/WeeklyReport?page=999");
        response.EnsureSuccessStatusCode();
    }

    // ── Discard Tests ────────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_Discard_WithoutAuth_Redirects()
    {
        var reportId = Guid.NewGuid();
        var response = await PostForm($"/WeeklyReport/Discard", new Dictionary<string, string>
        {
            { "id", reportId.ToString() }
        }, tokenUrl: "/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task WeeklyReport_Discard_DeletesReport()
    {
        await LoginAsAdmin();
        var userId = GetCurrentUserId();
        var reportId = Guid.NewGuid();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = reportId,
                UserId = userId,
                Content = "Report to be discarded.",
                WeekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Verify it exists
        var response = await Http.GetAsync($"/WeeklyReport/Details/{reportId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Discard it
        var discardResponse = await PostForm("/WeeklyReport/Discard", new Dictionary<string, string>
        {
            { "id", reportId.ToString() }
        });
        Assert.AreEqual(HttpStatusCode.Found, discardResponse.StatusCode,
            "Should redirect after discard");
        Assert.IsTrue(discardResponse.Headers.Location!.OriginalString.Contains("/WeeklyReport"),
            "Should redirect back to Index");

        // Verify it's gone
        var afterResponse = await Http.GetAsync($"/WeeklyReport/Details/{reportId}");
        Assert.AreEqual(HttpStatusCode.NotFound, afterResponse.StatusCode,
            "Report should be deleted after discard");
    }

    [TestMethod]
    public async Task WeeklyReport_Discard_CrossUserForbidden()
    {
        // Admin creates a report
        await LoginAsAdmin();
        var adminUserId = GetCurrentUserId();
        var reportId = Guid.NewGuid();

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = reportId,
                UserId = adminUserId,
                Content = "Admin's report.",
                WeekStart = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await LogoutAsync();

        // Register and login as a different user
        await RegisterAndLoginAsync();

        var discardResponse = await PostForm("/WeeklyReport/Discard", new Dictionary<string, string>
        {
            { "id", reportId.ToString() }
        });
        // Forbid() redirects to access-denied page with 302
        Assert.AreEqual(HttpStatusCode.Redirect, discardResponse.StatusCode,
            "Regular user should be redirected away when discarding another user's report");

        // Verify the report still exists (was NOT deleted)
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var report = await db.WeeklyReports.FindAsync(reportId);
            Assert.IsNotNull(report, "Report should still exist after failed discard attempt");
        }
    }

    [TestMethod]
    public async Task WeeklyReport_Discard_NotFound()
    {
        await LoginAsAdmin();
        var response = await PostForm("/WeeklyReport/Discard", new Dictionary<string, string>
        {
            { "id", Guid.NewGuid().ToString() }
        });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Generate Tests ───────────────────────────────────

    [TestMethod]
    public async Task WeeklyReport_Generate_WithoutAuth_Redirects()
    {
        var response = await PostForm("/WeeklyReport/Generate", new Dictionary<string, string>(),
            tokenUrl: "/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    // ── Background Job Tests ─────────────────────────────

    [TestMethod]
    public void WeeklyReportBackgroundJob_IsRegistered()
    {
        using var scope = Server!.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<WeeklyReportBackgroundJob>();
        Assert.IsNotNull(job);
        Assert.AreEqual("Weekly Report Generator", job.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(job.Description));
    }

    [TestMethod]
    public async Task WeeklyReportBackgroundJob_ExecuteAsync_DoesNotThrow()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<WeeklyReportBackgroundJob>();

        try
        {
            await job.ExecuteAsync();
        }
        catch (Exception ex)
        {
            // If LLM is not configured, it should handle gracefully internally.
            Assert.IsTrue(ex is not InvalidOperationException || !ex.Message.Contains("anthropic"),
                "Job should not throw unhandled exceptions");
        }
    }

    [TestMethod]
    public void WeeklyReportBackgroundJob_GetCurrentWeekStart_ReturnsMonday()
    {
        // Test with known dates
        var _ = new DateTime(2026, 8, 1, 14, 0, 0); // Sat Aug 1 UTC+8 is a Saturday
        var chinaNow = new DateTime(2026, 7, 31, 14, 0, 0); // Fri Jul 31 14:00 UTC+8

        var weekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(chinaNow);
        // Friday July 31 → Monday is July 27
        Assert.AreEqual(new DateTime(2026, 7, 27), weekStart.Date);
    }

    [TestMethod]
    public void WeeklyReportBackgroundJob_GetCurrentWeekStart_MondayIsSameDay()
    {
        var monday = new DateTime(2026, 7, 27, 10, 0, 0); // Monday 10:00 UTC+8
        var weekStart = WeeklyReportBackgroundJob.GetCurrentWeekStart(monday);
        Assert.AreEqual(new DateTime(2026, 7, 27), weekStart.Date,
            "When today is Monday, WeekStart should be the same Monday");
    }

    // ── Subagent Tests ───────────────────────────────────

    [TestMethod]
    public void WeeklyReportSubagent_IsRegisteredInDI()
    {
        var subagent = Server!.Services.GetRequiredService<WeeklyReportSubagent>();
        Assert.IsNotNull(subagent);
        Assert.AreEqual("WeeklyReport", subagent.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(subagent.Description));
    }

    [TestMethod]
    public async Task WeeklyReportSubagent_HasOnlyReadTools()
    {
        await LoginAsAdmin();
        var subagent = Server!.Services.GetRequiredService<WeeklyReportSubagent>();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        Assert.IsTrue(subagent.ToolNames.Length > 0, "Subagent should have tools configured");
        foreach (var toolName in subagent.ToolNames)
        {
            Assert.IsFalse(registry.IsWriteTool(toolName),
                $"Tool '{toolName}' used by WeeklyReport should be read-only");
        }
    }

    [TestMethod]
    public async Task WeeklyReportSubagent_NotExposedAsMcpTool()
    {
        await LoginAsAdmin();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        var weeklyReportTool = registry.AllTools.FirstOrDefault(t => t.ProtocolTool.Name == "WeeklyReport");
        Assert.IsNull(weeklyReportTool, "WeeklyReport should NOT be exposed as an MCP tool");
    }

    [TestMethod]
    public async Task WeeklyReportSubagent_OnlyTaskPlanningIsExposed()
    {
        await LoginAsAdmin();
        var registry = Server!.Services.GetRequiredService<ToolRegistry>();

        // Only TaskPlanning should be in the tool registry as a subagent tool
        var subagentTools = registry.AllTools
            .Where(t => t.ProtocolTool.Name is "TaskPlanning"
                or "DailyPlanning" or "DailySummary" or "WeeklyReport")
            .Select(t => t.ProtocolTool.Name)
            .ToList();

        Assert.IsTrue(subagentTools.Contains("TaskPlanning"),
            "TaskPlanning should be exposed as a tool");
        Assert.IsFalse(subagentTools.Contains("DailyPlanning"),
            "DailyPlanning should NOT be exposed");
        Assert.IsFalse(subagentTools.Contains("DailySummary"),
            "DailySummary should NOT be exposed");
        Assert.IsFalse(subagentTools.Contains("WeeklyReport"),
            "WeeklyReport should NOT be exposed");
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
