using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity;
using static Aiursoft.WebTools.Extends;
using System.Net;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class RolesControllerTests : TestBase
{
    [TestInitialize]
    public override async Task CreateServer()
    {
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>([], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(Server);
            using (var scope = Server.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Database.EnsureDeletedAsync();
            }
            await Server.UpdateDbAsync<TemplateDbContext>();
            await Server.SeedAsync();
            await Server.StartAsync();
        }
        catch
        {
            Server.Dispose();
            Server = null;
            throw;
        }
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server == null) return;
        await Server.StopAsync();
        using (var scope = Server.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Database.EnsureDeletedAsync();
        }
        Server.Dispose();
    }

    [TestMethod]
    [DataRow("claims")]
    [DataRow("membership")]
    [DataRow("role")]
    [DataRow("user")]
    public async Task ExistingDatabaseSeedingPreservesAdministratorRevocations(string revoked)
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = (await users.FindByNameAsync("admin"))!;
            var role = (await roles.FindByNameAsync("Administrators"))!;
            switch (revoked)
            {
                case "claims":
                    foreach (var claim in await roles.GetClaimsAsync(role))
                        Assert.IsTrue((await roles.RemoveClaimAsync(role, claim)).Succeeded);
                    break;
                case "membership":
                    Assert.IsTrue((await users.RemoveFromRoleAsync(user, "Administrators")).Succeeded);
                    break;
                case "role":
                    Assert.IsTrue((await roles.DeleteAsync(role)).Succeeded);
                    break;
                default:
                    Assert.IsTrue((await users.DeleteAsync(user)).Succeeded);
                    break;
            }
        }
        await Server.SeedAsync();
        using var verification = Server.Services.CreateScope();
        var savedUsers = verification.ServiceProvider.GetRequiredService<UserManager<User>>();
        var savedRoles = verification.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var savedUser = await savedUsers.FindByNameAsync("admin");
        var savedRole = await savedRoles.FindByNameAsync("Administrators");
        if (revoked == "user") Assert.IsNull(savedUser);
        else Assert.IsNotNull(savedUser);
        if (revoked == "role") Assert.IsNull(savedRole);
        else Assert.IsNotNull(savedRole);
        if (revoked == "claims") Assert.AreEqual(0, (await savedRoles.GetClaimsAsync(savedRole!)).Count);
        if (revoked == "membership") Assert.IsFalse(await savedUsers.IsInRoleAsync(savedUser!, "Administrators"));
    }

    [TestMethod]
    public async Task FreshDatabaseSeedsAllAdministratorPermissionsOnce()
    {
        await Server!.SeedAsync();
        using var scope = Server!.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = (await users.FindByNameAsync("admin"))!;
        var role = (await roles.FindByNameAsync("Administrators"))!;
        Assert.IsTrue(await users.IsInRoleAsync(user, "Administrators"));
        var claims = (await roles.GetClaimsAsync(role)).Where(claim => claim.Type == AppPermissions.Type)
            .Select(claim => claim.Value).ToArray();
        CollectionAssert.AreEquivalent(AppPermissions.GetAllPermissions().Select(permission => permission.Key).ToArray(), claims);
    }

    [TestMethod]
    public async Task TestRolesWorkflow()
    {
        await LoginAsAdmin();

        // 1. Index
        var indexResponse = await Http.GetAsync("/Roles/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Administrators", indexHtml);

        // 2. Create
        var roleName = "TestRole-" + Guid.NewGuid();
        var createResponse = await PostForm("/Roles/Create", new Dictionary<string, string>
        {
            { "RoleName", roleName }
        });
        AssertRedirect(createResponse, "/Roles/Details/", exact: false);
        var roleId = createResponse.Headers.Location?.OriginalString.Split("/").Last();
        Assert.IsNotNull(roleId);

        // 3. Details
        var detailsResponse = await Http.GetAsync($"/Roles/Details/{roleId}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(roleName, detailsHtml);

        // 4. Edit (GET)
        var editPageResponse = await Http.GetAsync($"/Roles/Edit/{roleId}");
        editPageResponse.EnsureSuccessStatusCode();

        // 5. Edit (POST)
        var newRoleName = roleName + "-Edited";
        var editContent = new Dictionary<string, string>
        {
            { "Id", roleId },
            { "RoleName", newRoleName },
            { "Claims[0].Key", "CanReadPermissions" },
            { "Claims[0].Name", "CanReadPermissions" },
            { "Claims[0].Description", "CanReadPermissions" },
            { "Claims[0].IsSelected", "true" }
        };
        var editResponse = await PostForm($"/Roles/Edit/{roleId}", editContent);
        AssertRedirect(editResponse, "/Roles/Details/", exact: false);

        // Verify edit
        var detailsResponse2 = await Http.GetAsync($"/Roles/Details/{roleId}");
        var detailsHtml2 = await detailsResponse2.Content.ReadAsStringAsync();
        Assert.Contains(newRoleName, detailsHtml2);

        // 6. Delete (GET)
        var deletePageResponse = await Http.GetAsync($"/Roles/Delete/{roleId}");
        deletePageResponse.EnsureSuccessStatusCode();

        // 7. Delete (POST)
        var deleteResponse = await PostForm($"/Roles/Delete/{roleId}", new Dictionary<string, string>
        {
            { "id", roleId }
        });
        AssertRedirect(deleteResponse, "/Roles");

        // Verify delete
        var indexResponse2 = await Http.GetAsync("/Roles/Index");
        var indexHtml2 = await indexResponse2.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newRoleName, indexHtml2);
    }

    [TestMethod]
    public async Task TestCreateRoleFailure()
    {
        await LoginAsAdmin();

        // Test with invalid model (empty name)
        var response = await PostForm("/Roles/Create", new Dictionary<string, string>
        {
            { "RoleName", "" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditRoleFailure()
    {
        await LoginAsAdmin();
        var roleName = "TestRole-" + Guid.NewGuid();
        await PostForm("/Roles/Create", new Dictionary<string, string> { { "RoleName", roleName } });

        var indexResponse = await Http.GetAsync("/Roles/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        // Extract role ID from HTML
        var match = System.Text.RegularExpressions.Regex.Match(indexHtml, @"/Roles/Details/([^""]+)");
        var roleId = match.Groups[1].Value;

        // Test with invalid model (empty name)
        var response = await PostForm($"/Roles/Edit/{roleId}", new Dictionary<string, string>
        {
            { "Id", roleId },
            { "RoleName", "" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Details/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNullId()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Details/");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteConfirmedNotFound()
    {
        await LoginAsAdmin();
        var response = await PostForm("/Roles/Delete/invalid-id", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Edit/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Delete/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
