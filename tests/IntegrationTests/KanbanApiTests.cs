using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Claims;
using Aiursoft.Kanban.Controllers.Api;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.SDK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Tests.IntegrationTests;

[TestClass]
public sealed class KanbanApiTests : TestBase
{
    [TestMethod]
    public async Task ConfigurationUsesAiurProtocolAndExplainsCurrentAuthMode()
    {
        var response = await Http.GetAsync("/api/v1/config");
        response.EnsureSuccessStatusCode();
        var model = JsonConvert.DeserializeObject<MobileConfigurationResponse>(
            await response.Content.ReadAsStringAsync());

        Assert.IsNotNull(model);
        Assert.AreEqual("Local", model.AuthenticationMode);
        Assert.IsTrue(model.AllowRegistration);
        Assert.AreEqual(0, (int)model.Code);
        Assert.IsNotNull(model.ProtocolVersion);
    }

    [TestMethod]
    public async Task LocalMobileLoginIssuesBearerTokenThatCanReadBoards()
    {
        using var login = await Http.PostAsync(
            "/api/v1/auth/local/login",
            Json(new LocalLoginRequest
            {
                EmailOrUserName = "admin@default.com",
                Password = "Admin@123456!"
            }));
        login.EnsureSuccessStatusCode();
        var authentication = JsonConvert.DeserializeObject<LocalAuthenticationResponse>(
            await login.Content.ReadAsStringAsync());
        Assert.IsNotNull(authentication);
        Assert.StartsWith("local.", authentication.AccessToken);
        Assert.IsGreaterThan(DateTimeOffset.UtcNow, authentication.ExpiresAt);

        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        using var create = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Created from local mobile token" }));
        create.EnsureSuccessStatusCode();
        var created = JsonConvert.DeserializeObject<BoardResponse>(
            await create.Content.ReadAsStringAsync());
        Assert.IsNotNull(created);

        using var boards = await Http.GetAsync("/api/v1/boards");
        boards.EnsureSuccessStatusCode();
        var response = JsonConvert.DeserializeObject<BoardListResponse>(
            await boards.Content.ReadAsStringAsync());
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Boards.Any(board => board.Id == created.Board.Id));
    }

    [TestMethod]
    public async Task LocalMobileRegistrationCreatesUsableAccount()
    {
        var email = $"android-{Guid.NewGuid():N}@example.com";
        using var registration = await Http.PostAsync(
            "/api/v1/auth/local/register",
            Json(new LocalRegistrationRequest
            {
                Email = email,
                Password = "phone-test-password"
            }));
        registration.EnsureSuccessStatusCode();
        var authentication = JsonConvert.DeserializeObject<LocalAuthenticationResponse>(
            await registration.Content.ReadAsStringAsync());
        Assert.IsNotNull(authentication);

        Assert.IsNotNull(Server);
        await using var scope = Server.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
            .FindByEmailAsync(email);
        Assert.IsNotNull(user);
        Assert.AreEqual(authentication.DisplayName, user.DisplayName);
    }

    [TestMethod]
    public async Task BoardsRejectsRequestsWithoutBearerToken()
    {
        var response = await Http.GetAsync("/api/v1/boards");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ApiCreatesBoardAndCardThenMovesCard()
    {
        Assert.IsNotNull(Server);
        await using var scope = Server.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        Assert.IsNotNull(admin);
        var controller = new KanbanApiController(
            db,
            userManager,
            services.GetRequiredService<KanbanApiAccessService>(),
            services.GetRequiredService<IOptions<Configuration.AppSettings>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, admin.Id)], "Bearer"))
                }
            }
        };

        var createdBoardResult = await controller.CreateBoard(new CreateBoardRequest { Name = "Android" });
        var createdBoard = AssertProtocol<BoardResponse>(createdBoardResult).Board;
        Assert.HasCount(3, createdBoard.Columns);

        var todo = createdBoard.Columns.Single(column => column.Status == nameof(ColumnStatus.NotStarted));
        var done = createdBoard.Columns.Single(column => column.Status == nameof(ColumnStatus.Completed));
        var createdCardResult = await controller.CreateCard(todo.Id, new CreateCardRequest
        {
            Title = "Test on phone",
            Description = "Created through the mobile API"
        });
        var card = AssertProtocol<CardResponse>(createdCardResult).Card;
        Assert.AreEqual(todo.Id, card.ColumnId);

        var movedCardResult = await controller.MoveCard(card.Id, new MoveCardRequest
        {
            TargetColumnId = done.Id,
            NewOrder = 0
        });
        Assert.AreEqual(done.Id, AssertProtocol<CardResponse>(movedCardResult).Card.ColumnId);

        var loaded = AssertProtocol<BoardResponse>(await controller.Board(createdBoard.Id)).Board;
        Assert.AreEqual(card.Id, loaded.Columns.Single(column => column.Id == done.Id).Cards.Single().Id);
    }

    [TestMethod]
    public async Task SharedBoardsAreListedSeparatelyAndExposeTheirEffectivePermission()
    {
        Assert.IsNotNull(Server);
        await using var scope = Server.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        Assert.IsNotNull(admin);

        var owner = new User
        {
            UserName = $"owner-{Guid.NewGuid():N}@example.com",
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            DisplayName = "Board owner"
        };
        var createdOwner = await userManager.CreateAsync(owner, "Owner-password-123!");
        Assert.IsTrue(createdOwner.Succeeded);

        var sharedBoard = new KanbanBoard
        {
            Name = "Shared mobile board",
            UserId = owner.Id,
            Order = 1
        };
        db.KanbanBoards.Add(sharedBoard);
        await db.SaveChangesAsync();
        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = sharedBoard.Id,
            SharedWithUserId = admin.Id,
            Permission = SharePermission.ReadOnly
        });
        await db.SaveChangesAsync();

        var controller = new KanbanApiController(
            db,
            userManager,
            services.GetRequiredService<KanbanApiAccessService>(),
            services.GetRequiredService<IOptions<Configuration.AppSettings>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, admin.Id)], "Bearer"))
                }
            }
        };

        var listed = AssertProtocol<BoardListResponse>(await controller.Boards()).Boards
            .Single(board => board.Id == sharedBoard.Id);
        Assert.IsFalse(listed.IsOwner);
        Assert.IsFalse(listed.CanEdit);

        var loaded = AssertProtocol<BoardResponse>(await controller.Board(sharedBoard.Id)).Board;
        Assert.IsFalse(loaded.IsOwner);
        Assert.IsFalse(loaded.CanEdit);

        var share = await db.BoardShares.SingleAsync(item => item.BoardId == sharedBoard.Id);
        share.Permission = SharePermission.Editable;
        await db.SaveChangesAsync();
        var editable = AssertProtocol<BoardResponse>(await controller.Board(sharedBoard.Id)).Board;
        Assert.IsTrue(editable.CanEdit);
    }

    private static T AssertProtocol<T>(IActionResult result) where T : class
    {
        var json = result as JsonResult;
        Assert.IsNotNull(json);
        var value = json.Value as T;
        Assert.IsNotNull(value);
        return value;
    }

    private static StringContent Json(object value) =>
        new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
}
