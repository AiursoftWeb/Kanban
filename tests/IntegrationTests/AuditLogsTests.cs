using System.Net;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Auditing;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class AuditLogsTests : TestBase
{
    [TestMethod]
    public async Task SuccessfulKanbanWriteIsAddedToAuditBuffer()
    {
        await LoginAsAdmin();
        var buffer = GetService<AuditLogBuffer>();
        buffer.Drain([]);

        var response = await PostForm("/Kanban/CreateBoard", new Dictionary<string, string>
        {
            ["name"] = "Audited board"
        });

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var logs = new List<AuditLog>();
        buffer.Drain(logs);
        Assert.IsTrue(logs.Any(log =>
            log.Action == "Kanban.CreateBoard" &&
            log.Details.Contains("Audited board", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UserCanOpenOwnLogsButNotAllUsersLogs()
    {
        await RegisterAndLoginAsync();

        var mineResponse = await Http.GetAsync("/AuditLogs/Mine");
        var allResponse = await Http.GetAsync("/AuditLogs/All");

        Assert.AreEqual(HttpStatusCode.OK, mineResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, allResponse.StatusCode);
        Assert.Contains("/Error/Code403", allResponse.Headers.Location?.OriginalString ?? string.Empty);
    }

    [TestMethod]
    public async Task AdministratorCanOpenAllUsersLogs()
    {
        await LoginAsAdmin();

        var response = await Http.GetAsync("/AuditLogs/All");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
