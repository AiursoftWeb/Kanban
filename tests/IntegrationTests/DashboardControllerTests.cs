namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public class DashboardControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        await LoginAsAdmin();
        var url = "/Dashboard/Index";
        
        var response = await Http.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        
        response.EnsureSuccessStatusCode();
        Assert.Contains("Kanban Dashboard", html);
    }
}
