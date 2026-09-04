using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Claims;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.Kanban.Configuration;
using Aiursoft.Kanban.Controllers.Api;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.SDK.Models;
using MediatR;
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
    public async Task CardImageUploadGrantAcceptsValidRasterImages()
    {
        await AuthenticateLocalAsync();

        using var grantResponse = await Http.GetAsync("/api/v1/uploads/card-images");
        grantResponse.EnsureSuccessStatusCode();
        var grant = JsonConvert.DeserializeObject<CardImageUploadGrantResponse>(
            await grantResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(grant);
        Assert.AreEqual(10, grant.MaxSizeInMb);
        CollectionAssert.Contains(grant.AllowedExtensions, "png");
        Assert.StartsWith("/upload/kanban-images?token=", grant.UploadUrl);

        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        using var multipart = new MultipartFormDataContent();
        var image = new ByteArrayContent(pngBytes);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(image, "file", $"android-{Guid.NewGuid():N}.png");

        using var uploadResponse = await Http.PostAsync(grant.UploadUrl, multipart);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = JsonConvert.DeserializeObject<CardImageUploadResponse>(
            await uploadResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(uploaded);
        Assert.StartsWith("kanban-images/", uploaded.Path);
        StringAssert.Contains(uploaded.InternetPath, "/download/kanban-images/");
    }

    [TestMethod]
    public async Task AgentApiRequiresBearerAndRejectsInvalidBoardAndWorkbook()
    {
        using var anonymous = await Http.GetAsync($"/api/v1/agent/conversations/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await AuthenticateLocalAsync();
        using var invalidBoard = await Http.PostAsync(
            "/api/v1/agent/messages",
            Json(new AgentSendMessageRequest
            {
                BoardId = int.MaxValue,
                Message = "Show this board"
            }));
        Assert.AreEqual(HttpStatusCode.NotFound, invalidBoard.StatusCode);

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent("not a workbook"u8.ToArray()), "file", "planning.xls");
        using var invalidWorkbook = await Http.PostAsync("/api/v1/agent/excel", multipart);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidWorkbook.StatusCode);
        var error = JsonConvert.DeserializeObject<AiurResponse>(
            await invalidWorkbook.Content.ReadAsStringAsync());
        Assert.IsNotNull(error);
        Assert.AreEqual(Code.InvalidInput, error.Code);
    }

    [TestMethod]
    public async Task AgentApiStartsReadsAndCancelsOwnedConversation()
    {
        await AuthenticateLocalAsync();
        using var startResponse = await Http.PostAsync(
            "/api/v1/agent/messages",
            Json(new AgentSendMessageRequest
            {
                Message = "List my urgent work"
            }));
        startResponse.EnsureSuccessStatusCode();
        var started = JsonConvert.DeserializeObject<AgentConversationResponse>(
            await startResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(started);
        Assert.AreNotEqual(Guid.Empty, started.ConversationId);

        using var statusResponse = await Http.GetAsync(
            $"/api/v1/agent/conversations/{started.ConversationId}");
        statusResponse.EnsureSuccessStatusCode();
        var status = JsonConvert.DeserializeObject<AgentStatusResponse>(
            await statusResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(status);
        Assert.AreEqual(started.ConversationId, status.ConversationId);
        Assert.IsTrue(status.Messages.Any(message =>
            message.Role == "user" && message.Content == "List my urgent work"));

        using var cancelResponse = await Http.PostAsync(
            $"/api/v1/agent/conversations/{started.ConversationId}/cancel",
            Json(new { }));
        cancelResponse.EnsureSuccessStatusCode();
        Assert.IsNull(GetService<IAgentService>().GetConversation(started.ConversationId));
    }

    [TestMethod]
    public async Task ReportsRejectRequestsWithoutBearerToken()
    {
        using var daily = await Http.GetAsync("/api/v1/reports/daily");
        using var weekly = await Http.GetAsync("/api/v1/reports/weekly");

        Assert.AreEqual(HttpStatusCode.Unauthorized, daily.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, weekly.StatusCode);
    }

    [TestMethod]
    public async Task ReportApiListsAndLoadsOnlyTheAuthenticatedUsersReports()
    {
        Assert.IsNotNull(Server);

        var mobileEmail = $"reports-{Guid.NewGuid():N}@example.com";
        const string mobilePassword = "Reports-password-123!";
        Guid dailyId;
        Guid weeklyId;
        Guid anotherUsersDailyId;
        var todayChina = (DateTime.UtcNow + TimeSpan.FromHours(8)).Date;
        var currentWeekStart = todayChina.AddDays(
            -((7 + (int)todayChina.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<TemplateDbContext>();
            var userManager = services.GetRequiredService<UserManager<User>>();
            var mobileUser = new User
            {
                UserName = mobileEmail,
                Email = mobileEmail,
                DisplayName = "Mobile report reader"
            };
            var userCreation = await userManager.CreateAsync(mobileUser, mobilePassword);
            Assert.IsTrue(userCreation.Succeeded);

            dailyId = Guid.NewGuid();
            weeklyId = Guid.NewGuid();
            anotherUsersDailyId = Guid.NewGuid();
            db.DailyReports.AddRange(
                new DailyReport
                {
                    Id = dailyId,
                    UserId = mobileUser.Id,
                    ReportType = DailyReportType.Plan,
                    Content = "Android daily plan",
                    Date = todayChina,
                    GeneratedAt = new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc)
                },
                new DailyReport
                {
                    Id = anotherUsersDailyId,
                    UserId = "another-mobile-user",
                    ReportType = DailyReportType.Summary,
                    Content = "Private report from another user",
                    Date = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc),
                    GeneratedAt = new DateTime(2026, 9, 3, 2, 0, 0, DateTimeKind.Utc)
                });
            db.WeeklyReports.Add(new WeeklyReport
            {
                Id = weeklyId,
                UserId = mobileUser.Id,
                Content = "Android weekly report",
                WeekStart = currentWeekStart,
                GeneratedAt = new DateTime(2026, 9, 3, 3, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateLocalAsync(mobileEmail, mobilePassword);

        using var dailyListResponse = await Http.GetAsync("/api/v1/reports/daily?page=-8");
        dailyListResponse.EnsureSuccessStatusCode();
        var dailyList = JsonConvert.DeserializeObject<DailyReportListResponse>(
            await dailyListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(dailyList);
        Assert.AreEqual(1, dailyList.CurrentPage);
        Assert.IsTrue(dailyList.Reports.Any(report => report.Id == dailyId));
        Assert.IsFalse(dailyList.Reports.Any(report => report.Id == anotherUsersDailyId));
        Assert.AreEqual("Plan", dailyList.Reports.Single(report => report.Id == dailyId).ReportType);
        Assert.AreEqual(dailyId, dailyList.TodayPlan?.Id);

        using var dailyDetailsResponse = await Http.GetAsync($"/api/v1/reports/daily/{dailyId}");
        dailyDetailsResponse.EnsureSuccessStatusCode();
        var dailyDetails = JsonConvert.DeserializeObject<DailyReportResponse>(
            await dailyDetailsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(dailyDetails);
        Assert.AreEqual("Android daily plan", dailyDetails.Report.Content);

        using var weeklyListResponse = await Http.GetAsync("/api/v1/reports/weekly");
        weeklyListResponse.EnsureSuccessStatusCode();
        var weeklyList = JsonConvert.DeserializeObject<WeeklyReportListResponse>(
            await weeklyListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(weeklyList);
        Assert.AreEqual(weeklyId, weeklyList.Reports.Single().Id);
        Assert.AreEqual(weeklyId, weeklyList.CurrentWeekReport?.Id);
        Assert.AreEqual(currentWeekStart, weeklyList.CurrentWeekStart);

        using var weeklyDetailsResponse = await Http.GetAsync($"/api/v1/reports/weekly/{weeklyId}");
        weeklyDetailsResponse.EnsureSuccessStatusCode();
        var weeklyDetails = JsonConvert.DeserializeObject<WeeklyReportResponse>(
            await weeklyDetailsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(weeklyDetails);
        Assert.AreEqual("Android weekly report", weeklyDetails.Report.Content);

        using var discardResponse = await Http.DeleteAsync($"/api/v1/reports/weekly/{weeklyId}");
        discardResponse.EnsureSuccessStatusCode();
        using var discardedDetailsResponse = await Http.GetAsync($"/api/v1/reports/weekly/{weeklyId}");
        Assert.AreEqual(HttpStatusCode.NotFound, discardedDetailsResponse.StatusCode);

        using var privateDetailsResponse = await Http.GetAsync(
            $"/api/v1/reports/daily/{anotherUsersDailyId}");
        Assert.AreEqual(HttpStatusCode.NotFound, privateDetailsResponse.StatusCode);
        var privateDetails = JsonConvert.DeserializeObject<AiurResponse>(
            await privateDetailsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(privateDetails);
        Assert.AreEqual(Code.NotFound, privateDetails.Code);
    }

    [TestMethod]
    public async Task MyTasksApiAppliesStatusLabelAndSortFiltersWithoutLeakingOtherUsersTasks()
    {
        var mobileEmail = $"tasks-{Guid.NewGuid():N}@example.com";
        const string mobilePassword = "Tasks-password-123!";
        Assert.IsNotNull(Server);

        string mobileUserId;
        string anotherUserId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var mobileUser = new User
            {
                UserName = mobileEmail,
                Email = mobileEmail,
                DisplayName = "Mobile task reader"
            };
            var anotherUser = new User
            {
                UserName = $"other-tasks-{Guid.NewGuid():N}@example.com",
                Email = $"other-tasks-{Guid.NewGuid():N}@example.com",
                DisplayName = "Other task reader"
            };
            Assert.IsTrue((await userManager.CreateAsync(mobileUser, mobilePassword)).Succeeded);
            Assert.IsTrue((await userManager.CreateAsync(anotherUser)).Succeeded);
            mobileUserId = mobileUser.Id;
            anotherUserId = anotherUser.Id;
        }

        await AuthenticateLocalAsync(mobileEmail, mobilePassword);
        using var createBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Mobile task filters" }));
        createBoard.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(
            await createBoard.Content.ReadAsStringAsync())!.Board;
        var notStartedColumn = board.Columns.Single(column => column.Status == nameof(ColumnStatus.NotStarted));
        var inProgressColumn = board.Columns.Single(column => column.Status == nameof(ColumnStatus.InProgress));

        using var firstCardResponse = await Http.PostAsync(
            $"/api/v1/columns/{inProgressColumn.Id}/cards",
            Json(new CreateCardRequest { Title = "Urgent mobile task" }));
        firstCardResponse.EnsureSuccessStatusCode();
        var firstCard = JsonConvert.DeserializeObject<CardResponse>(
            await firstCardResponse.Content.ReadAsStringAsync())!.Card;
        using var secondCardResponse = await Http.PostAsync(
            $"/api/v1/columns/{notStartedColumn.Id}/cards",
            Json(new CreateCardRequest { Title = "Unlabelled task" }));
        secondCardResponse.EnsureSuccessStatusCode();

        int labelId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var card = await db.KanbanCards.FindAsync(firstCard.Id);
            Assert.IsNotNull(card);
            card.Priority = Priority.Urgent;
            card.DueDate = DateTime.UtcNow.AddDays(1);
            var label = new KanbanLabel { Name = "Mobile", Color = "#2563EB" };
            db.KanbanLabels.Add(label);
            db.KanbanCardLabels.Add(new KanbanCardLabel { CardId = card.Id, Label = label });
            await db.SaveChangesAsync();
            labelId = label.Id;
        }

        using var filteredResponse = await Http.GetAsync(
            $"/api/v1/tasks/mine?status=in-progress&labelIds={labelId}&labelMode=all&sort=priority-desc");
        filteredResponse.EnsureSuccessStatusCode();
        var filtered = JsonConvert.DeserializeObject<MyTasksResponse>(
            await filteredResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(filtered);
        Assert.AreEqual(mobileUserId, filtered.TargetUser.Id);
        Assert.AreEqual(firstCard.Id, filtered.Cards.Single().Id);
        Assert.AreEqual(nameof(ColumnStatus.InProgress), filtered.Cards.Single().Status);
        Assert.AreEqual("Mobile", filtered.Cards.Single().Labels.Single().Name);
        Assert.AreEqual("in-progress", filtered.SelectedStatus);
        Assert.AreEqual("all", filtered.SelectedLabelMode);
        Assert.AreEqual("priority-desc", filtered.SelectedSort);

        using var otherUserResponse = await Http.GetAsync(
            $"/api/v1/tasks/mine?targetUserId={Uri.EscapeDataString(anotherUserId)}");
        Assert.AreEqual(HttpStatusCode.Unauthorized, otherUserResponse.StatusCode);
        var unauthorized = JsonConvert.DeserializeObject<AiurResponse>(
            await otherUserResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(unauthorized);
        Assert.AreEqual(Code.Unauthorized, unauthorized.Code);
    }

    [TestMethod]
    public async Task SearchApiFindsCardsOnlyOnReadableBoardsAndReturnsCardContext()
    {
        var searchEmail = $"search-{Guid.NewGuid():N}@example.com";
        const string searchPassword = "Search-password-123!";
        Assert.IsNotNull(Server);

        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User
            {
                UserName = searchEmail,
                Email = searchEmail,
                DisplayName = "Mobile search user"
            };
            Assert.IsTrue((await userManager.CreateAsync(user, searchPassword)).Succeeded);
        }

        await AuthenticateLocalAsync(searchEmail, searchPassword);
        using var boardResponse = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Searchable mobile board" }));
        boardResponse.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(
            await boardResponse.Content.ReadAsStringAsync())!.Board;
        using var cardResponse = await Http.PostAsync(
            $"/api/v1/columns/{board.Columns.First().Id}/cards",
            Json(new CreateCardRequest
            {
                Title = "Native parity milestone",
                Description = "Search this Android work item"
            }));
        cardResponse.EnsureSuccessStatusCode();
        var card = JsonConvert.DeserializeObject<CardResponse>(
            await cardResponse.Content.ReadAsStringAsync())!.Card;

        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var hiddenBoard = new KanbanBoard
            {
                Name = "Hidden search board",
                UserId = "another-search-user",
                Columns =
                [
                    new KanbanColumn
                    {
                        Name = "Hidden",
                        Cards =
                        [
                            new KanbanCard
                            {
                                Title = "Native parity must stay private",
                                CreatorUserId = "another-search-user"
                            }
                        ]
                    }
                ]
            };
            db.KanbanBoards.Add(hiddenBoard);
            await db.SaveChangesAsync();
        }

        using var response = await Http.GetAsync("/api/v1/search/cards?query=Native%20parity");
        response.EnsureSuccessStatusCode();
        var search = JsonConvert.DeserializeObject<CardSearchResponse>(
            await response.Content.ReadAsStringAsync());
        Assert.IsNotNull(search);
        Assert.IsFalse(search.UsedAi);
        Assert.AreEqual(1, search.TotalCount);
        Assert.AreEqual(card.Id, search.Cards.Single().Id);
        Assert.AreEqual(board.Id, search.Cards.Single().BoardId);
        Assert.AreEqual("Searchable mobile board", search.Cards.Single().BoardName);

        using var emptyResponse = await Http.GetAsync("/api/v1/search/cards?query=%20%20");
        emptyResponse.EnsureSuccessStatusCode();
        var empty = JsonConvert.DeserializeObject<CardSearchResponse>(
            await emptyResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(empty);
        Assert.AreEqual(0, empty.TotalCount);
        Assert.IsEmpty(empty.Cards);
    }

    [TestMethod]
    public async Task DashboardApiMatchesWebOverviewCountsTasksBoardsAndTodayReports()
    {
        var email = $"dashboard-{Guid.NewGuid():N}@example.com";
        const string password = "Dashboard-password-123!";
        Assert.IsNotNull(Server);

        string userId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User
            {
                UserName = email,
                Email = email,
                DisplayName = "Mobile dashboard user"
            };
            Assert.IsTrue((await userManager.CreateAsync(user, password)).Succeeded);
            userId = user.Id;
        }

        await AuthenticateLocalAsync(email, password);
        using var boardResponse = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Dashboard own board" }));
        boardResponse.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(
            await boardResponse.Content.ReadAsStringAsync())!.Board;
        var inProgress = board.Columns.Single(column => column.Status == nameof(ColumnStatus.InProgress));
        using var cardResponse = await Http.PostAsync(
            $"/api/v1/columns/{inProgress.Id}/cards",
            Json(new CreateCardRequest { Title = "Dashboard active task" }));
        cardResponse.EnsureSuccessStatusCode();
        var card = JsonConvert.DeserializeObject<CardResponse>(
            await cardResponse.Content.ReadAsStringAsync())!.Card;

        Guid planId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var storedCard = await db.KanbanCards.FindAsync(card.Id);
            Assert.IsNotNull(storedCard);
            storedCard.Priority = Priority.High;
            storedCard.DueDate = DateTime.UtcNow.AddMinutes(-10);
            var sharedBoard = new KanbanBoard
            {
                Name = "Dashboard shared board",
                UserId = "dashboard-other-owner",
                Columns = [new KanbanColumn { Name = "To Do" }]
            };
            db.KanbanBoards.Add(sharedBoard);
            await db.SaveChangesAsync();
            db.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(),
                BoardId = sharedBoard.Id,
                SharedWithUserId = userId,
                Permission = SharePermission.ReadOnly
            });
            planId = Guid.NewGuid();
            db.DailyReports.Add(new DailyReport
            {
                Id = planId,
                UserId = userId,
                ReportType = DailyReportType.Plan,
                Content = "Dashboard plan content",
                Date = (DateTime.UtcNow + TimeSpan.FromHours(8)).Date,
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await Http.GetAsync("/api/v1/dashboard");
        response.EnsureSuccessStatusCode();
        var dashboard = JsonConvert.DeserializeObject<DashboardResponse>(
            await response.Content.ReadAsStringAsync());
        Assert.IsNotNull(dashboard);
        Assert.AreEqual(1, dashboard.OwnedBoardCount);
        Assert.AreEqual(1, dashboard.SharedBoardCount);
        Assert.AreEqual(1, dashboard.AssignedTaskCount);
        Assert.AreEqual(1, dashboard.OverdueTaskCount);
        Assert.AreEqual(1, dashboard.InProgressTaskCount);
        Assert.AreEqual(card.Id, dashboard.AssignedTasks.Single().Id);
        Assert.AreEqual(board.Id, dashboard.OwnedBoards.Single().BoardId);
        Assert.AreEqual("ReadOnly", dashboard.SharedBoards.Single().Permission);
        Assert.AreEqual(planId, dashboard.LatestPlan?.Id);
        Assert.IsNull(dashboard.LatestSummary);
    }

    [TestMethod]
    public async Task NotificationApiListsAndMarksOnlyCurrentUsersUnreadNotifications()
    {
        var email = $"notifications-{Guid.NewGuid():N}@example.com";
        const string password = "Notifications-password-123!";
        Assert.IsNotNull(Server);

        string userId;
        string actorId;
        int ownNotificationId;
        int otherNotificationId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<User>>();
            var user = new User
            {
                UserName = email,
                Email = email,
                DisplayName = "Mobile notification user"
            };
            var actor = new User
            {
                UserName = $"actor-{Guid.NewGuid():N}@example.com",
                Email = $"actor-{Guid.NewGuid():N}@example.com",
                DisplayName = "Notification actor"
            };
            Assert.IsTrue((await userManager.CreateAsync(user, password)).Succeeded);
            Assert.IsTrue((await userManager.CreateAsync(actor)).Succeeded);
            userId = user.Id;
            actorId = actor.Id;

            var db = services.GetRequiredService<TemplateDbContext>();
            var own = new Notification
            {
                UserId = userId,
                ActorUserId = actorId,
                Type = NotificationType.BoardShared,
                Message = "Notification actor shared a board with you"
            };
            var alreadyRead = new Notification
            {
                UserId = userId,
                ActorUserId = actorId,
                Type = NotificationType.CardUpdated,
                Message = "Already read",
                IsRead = true
            };
            var secondOwn = new Notification
            {
                UserId = userId,
                ActorUserId = actorId,
                Type = NotificationType.CardUpdated,
                Message = "A second unread notification"
            };
            var other = new Notification
            {
                UserId = actorId,
                ActorUserId = userId,
                Type = NotificationType.BoardShared,
                Message = "Private notification for actor"
            };
            db.Notifications.AddRange(own, alreadyRead, secondOwn, other);
            await db.SaveChangesAsync();
            ownNotificationId = own.Id;
            otherNotificationId = other.Id;
        }

        await AuthenticateLocalAsync(email, password);
        using var listResponse = await Http.GetAsync("/api/v1/notifications");
        listResponse.EnsureSuccessStatusCode();
        var list = JsonConvert.DeserializeObject<NotificationListResponse>(
            await listResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(list);
        Assert.AreEqual(2, list.UnreadCount);
        Assert.Contains(ownNotificationId, list.Notifications.Select(item => item.Id));
        Assert.IsTrue(list.Notifications.All(item => item.ActorUserName == "Notification actor"));

        using var forbiddenRead = await Http.PutAsync(
            $"/api/v1/notifications/{otherNotificationId}/read",
            Json(new { }));
        Assert.AreEqual(HttpStatusCode.NotFound, forbiddenRead.StatusCode);

        using var markRead = await Http.PutAsync(
            $"/api/v1/notifications/{ownNotificationId}/read",
            Json(new { }));
        markRead.EnsureSuccessStatusCode();
        using var remainingListResponse = await Http.GetAsync("/api/v1/notifications");
        remainingListResponse.EnsureSuccessStatusCode();
        var remainingList = JsonConvert.DeserializeObject<NotificationListResponse>(
            await remainingListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(remainingList);
        Assert.AreEqual(1, remainingList.UnreadCount);

        using var markAllRead = await Http.PutAsync(
            "/api/v1/notifications/read-all",
            Json(new { }));
        markAllRead.EnsureSuccessStatusCode();
        using var emptyListResponse = await Http.GetAsync("/api/v1/notifications");
        emptyListResponse.EnsureSuccessStatusCode();
        var emptyList = JsonConvert.DeserializeObject<NotificationListResponse>(
            await emptyListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(emptyList);
        Assert.AreEqual(0, emptyList.UnreadCount);
        Assert.IsEmpty(emptyList.Notifications);

        await using var verificationScope = Server.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse((await verificationDb.Notifications.FindAsync(otherNotificationId))!.IsRead);
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
            services.GetRequiredService<IOptions<AppSettings>>(),
            services.GetRequiredService<IMediator>(),
            services.GetRequiredService<ILogger<KanbanApiController>>())
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
            services.GetRequiredService<IOptions<AppSettings>>(),
            services.GetRequiredService<IMediator>(),
            services.GetRequiredService<ILogger<KanbanApiController>>())
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

    [TestMethod]
    public async Task ArchivedBoardApiListsOwnedAndSharedBoardsAndOnlyOwnerCanRestore()
    {
        await AuthenticateLocalAsync();
        using var createBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = $"Archive mobile {Guid.NewGuid():N}" }));
        createBoard.EnsureSuccessStatusCode();
        var ownedBoard = JsonConvert.DeserializeObject<BoardResponse>(
            await createBoard.Content.ReadAsStringAsync())!.Board;

        Assert.IsNotNull(Server);
        int sharedBoardId;
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<User>>();
            var admin = await userManager.FindByEmailAsync("admin@default.com");
            Assert.IsNotNull(admin);
            var owner = new User
            {
                UserName = $"archive-owner-{Guid.NewGuid():N}@example.com",
                Email = $"archive-owner-{Guid.NewGuid():N}@example.com",
                DisplayName = "Archive owner"
            };
            Assert.IsTrue((await userManager.CreateAsync(owner)).Succeeded);
            var db = services.GetRequiredService<TemplateDbContext>();
            var sharedBoard = new KanbanBoard
            {
                Name = "Archived shared mobile board",
                UserId = owner.Id,
                IsArchived = true,
                ArchivedTime = DateTime.UtcNow.AddDays(-1),
                Columns =
                [
                    new KanbanColumn
                    {
                        Name = "Backlog",
                        Order = 0,
                        ColumnStatus = ColumnStatus.NotStarted,
                        Cards =
                        [
                            new KanbanCard
                            {
                                Title = "Archived shared card",
                                Order = 0,
                                DueDate = DateTime.UtcNow.AddDays(-2)
                            }
                        ]
                    }
                ]
            };
            db.KanbanBoards.Add(sharedBoard);
            db.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(),
                BoardId = sharedBoard.Id,
                Board = sharedBoard,
                SharedWithUserId = admin.Id,
                Permission = SharePermission.ReadOnly
            });
            await db.SaveChangesAsync();
            sharedBoardId = sharedBoard.Id;
        }

        using var archiveResponse = await Http.PutAsync(
            $"/api/v1/boards/{ownedBoard.Id}/archive",
            Json(new SetBoardArchiveRequest { Archive = true }));
        archiveResponse.EnsureSuccessStatusCode();
        var archiveResult = JsonConvert.DeserializeObject<BoardArchiveResponse>(
            await archiveResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(archiveResult);
        Assert.IsTrue(archiveResult.IsArchived);
        Assert.IsNotNull(archiveResult.ArchivedTime);

        using var archivedListResponse = await Http.GetAsync("/api/v1/boards/archived");
        archivedListResponse.EnsureSuccessStatusCode();
        var archivedList = JsonConvert.DeserializeObject<ArchivedBoardListResponse>(
            await archivedListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(archivedList);
        Assert.AreEqual(ownedBoard.Id,
            archivedList.OwnedBoards.Single(board => board.Id == ownedBoard.Id).Id);
        var shared = archivedList.SharedBoards.Single(board => board.Id == sharedBoardId);
        Assert.AreEqual(1, shared.IncompleteCount);
        Assert.AreEqual(1, shared.OverdueCount);
        Assert.AreEqual("ReadOnly", shared.Permission);
        Assert.AreEqual("Direct share", shared.SharedVia);

        using var activeListResponse = await Http.GetAsync("/api/v1/boards");
        activeListResponse.EnsureSuccessStatusCode();
        var activeList = JsonConvert.DeserializeObject<BoardListResponse>(
            await activeListResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(activeList);
        Assert.IsFalse(activeList.Boards.Any(board => board.Id == ownedBoard.Id));

        using var archivedDetailsResponse = await Http.GetAsync($"/api/v1/boards/{ownedBoard.Id}");
        archivedDetailsResponse.EnsureSuccessStatusCode();
        var archivedDetails = JsonConvert.DeserializeObject<BoardResponse>(
            await archivedDetailsResponse.Content.ReadAsStringAsync())!.Board;
        Assert.IsTrue(archivedDetails.IsArchived);
        Assert.IsFalse(archivedDetails.CanEdit);

        using var forbiddenRestore = await Http.PutAsync(
            $"/api/v1/boards/{sharedBoardId}/archive",
            Json(new SetBoardArchiveRequest { Archive = false }));
        var forbiddenResult = JsonConvert.DeserializeObject<AiurResponse>(
            await forbiddenRestore.Content.ReadAsStringAsync());
        Assert.IsNotNull(forbiddenResult);
        Assert.AreEqual(Code.Unauthorized, forbiddenResult.Code);

        using var restoreResponse = await Http.PutAsync(
            $"/api/v1/boards/{ownedBoard.Id}/archive",
            Json(new SetBoardArchiveRequest { Archive = false }));
        restoreResponse.EnsureSuccessStatusCode();
        var restoreResult = JsonConvert.DeserializeObject<BoardArchiveResponse>(
            await restoreResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(restoreResult);
        Assert.IsFalse(restoreResult.IsArchived);
        Assert.IsNull(restoreResult.ArchivedTime);
    }

    [TestMethod]
    public async Task CardApiSupportsDetailsUpdateCommentsSubscriptionAndDeletion()
    {
        await AuthenticateLocalAsync();

        using var createBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Android card details" }));
        createBoard.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(
            await createBoard.Content.ReadAsStringAsync())!.Board;
        var column = board.Columns.First();

        using var createCard = await Http.PostAsync(
            $"/api/v1/columns/{column.Id}/cards",
            Json(new CreateCardRequest { Title = "First title", Description = "Draft" }));
        createCard.EnsureSuccessStatusCode();
        var createdCard = JsonConvert.DeserializeObject<CardResponse>(
            await createCard.Content.ReadAsStringAsync())!.Card;

        Assert.IsNotNull(Server);
        string assigneeId;
        string assigneeUserName;
        await using (var memberScope = Server.Services.CreateAsyncScope())
        {
            var services = memberScope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<User>>();
            assigneeUserName = $"android-member-{Guid.NewGuid():N}@example.com";
            var member = new User
            {
                UserName = assigneeUserName,
                Email = $"android-member-{Guid.NewGuid():N}@example.com",
                DisplayName = "   "
            };
            var result = await userManager.CreateAsync(member);
            Assert.IsTrue(result.Succeeded);
            assigneeId = member.Id;
            var memberDb = services.GetRequiredService<TemplateDbContext>();
            memberDb.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                SharedWithUserId = member.Id,
                Permission = SharePermission.Editable
            });
            await memberDb.SaveChangesAsync();
        }

        using var detailsResponse = await Http.GetAsync($"/api/v1/cards/{createdCard.Id}");
        detailsResponse.EnsureSuccessStatusCode();
        var details = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await detailsResponse.Content.ReadAsStringAsync())!.Card;
        Assert.IsTrue(details.CanEdit);
        Assert.IsTrue(details.CanDelete);
        Assert.IsTrue(details.IsSubscribed);
        Assert.AreEqual(board.Id, details.BoardId);
        Assert.IsNotNull(details.AssignedUser);
        Assert.IsTrue(details.AvailableAssignees.Any(user => user.Id == details.AssignedUser.Id));
        Assert.IsTrue(details.AvailableAssignees.Any(user => user.Id == assigneeId));
        Assert.AreEqual(
            assigneeUserName,
            details.AvailableAssignees.Single(user => user.Id == assigneeId).DisplayName);
        Assert.HasCount(3, details.AvailableColumns);

        using var updateResponse = await Http.PutAsync(
            $"/api/v1/cards/{createdCard.Id}",
            Json(new UpdateCardRequest
            {
                Title = "Ready on Android",
                Description = "Edited from the native app",
                Priority = nameof(Priority.Urgent),
                AssignedUserId = assigneeId,
                DueDate = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                RecurrenceInterval = 2,
                RecurrenceUnit = nameof(RecurrenceUnit.Week)
            }));
        updateResponse.EnsureSuccessStatusCode();
        var updated = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await updateResponse.Content.ReadAsStringAsync())!.Card;
        Assert.AreEqual("Ready on Android", updated.Title);
        Assert.AreEqual(nameof(Priority.Urgent), updated.Priority);
        Assert.AreEqual(assigneeId, updated.AssignedUser?.Id);
        Assert.AreEqual(2, updated.RecurrenceInterval);
        Assert.AreEqual(nameof(RecurrenceUnit.Week), updated.RecurrenceUnit);

        var completedColumn = board.Columns.Single(item => item.Status == nameof(ColumnStatus.Completed));
        using var moveResponse = await Http.PutAsync(
            $"/api/v1/cards/{createdCard.Id}/position",
            Json(new MoveCardRequest
            {
                TargetColumnId = completedColumn.Id,
                NewOrder = 0
            }));
        moveResponse.EnsureSuccessStatusCode();
        var moved = JsonConvert.DeserializeObject<CardResponse>(
            await moveResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(moved);
        Assert.AreEqual(column.Id, moved.Card.ColumnId);
        StringAssert.Contains(moved.Message, "Recurring card reset");

        using var movedDetailsResponse = await Http.GetAsync($"/api/v1/cards/{createdCard.Id}");
        movedDetailsResponse.EnsureSuccessStatusCode();
        var movedDetails = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await movedDetailsResponse.Content.ReadAsStringAsync())!.Card;
        Assert.AreEqual(column.Id, movedDetails.ColumnId);
        Assert.AreEqual(new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc), movedDetails.DueDate);
        Assert.IsNull(movedDetails.ActualStartTime);
        Assert.IsNull(movedDetails.ActualEndTime);

        using var addLabelResponse = await Http.PostAsync(
            $"/api/v1/cards/{createdCard.Id}/labels",
            Json(new AddCardLabelRequest { Name = "Mobile" }));
        addLabelResponse.EnsureSuccessStatusCode();
        var addedLabel = JsonConvert.DeserializeObject<CardLabelResponse>(
            await addLabelResponse.Content.ReadAsStringAsync())!.Label;
        Assert.AreEqual("Mobile", addedLabel.Name);

        using var commentResponse = await Http.PostAsync(
            $"/api/v1/cards/{createdCard.Id}/comments",
            Json(new AddCardCommentRequest
            {
                Content = "Reply from Android",
                Images = "https://kanban.example/download/kanban-images/android.png"
            }));
        commentResponse.EnsureSuccessStatusCode();
        var comment = JsonConvert.DeserializeObject<CardCommentResponse>(
            await commentResponse.Content.ReadAsStringAsync())!.Comment;
        Assert.AreEqual("Reply from Android", comment.Content);
        Assert.AreEqual(
            "https://kanban.example/download/kanban-images/android.png",
            comment.Images);
        Assert.IsTrue(comment.CanDelete);

        using var refreshedResponse = await Http.GetAsync($"/api/v1/cards/{createdCard.Id}");
        refreshedResponse.EnsureSuccessStatusCode();
        var refreshed = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await refreshedResponse.Content.ReadAsStringAsync())!.Card;
        Assert.AreEqual(comment.Id, refreshed.Comments.Single().Id);
        Assert.AreEqual(comment.Images, refreshed.Comments.Single().Images);
        Assert.AreEqual(addedLabel.Id, refreshed.Labels.Single().Id);
        Assert.IsTrue(refreshed.AvailableLabels.Any(label => label.Id == addedLabel.Id));

        using var removeLabel = await Http.DeleteAsync(
            $"/api/v1/cards/{createdCard.Id}/labels/{addedLabel.Id}");
        removeLabel.EnsureSuccessStatusCode();

        using var unsubscribeResponse = await Http.PutAsync(
            $"/api/v1/cards/{createdCard.Id}/subscription",
            Json(new SetCardSubscriptionRequest { Subscribe = false }));
        unsubscribeResponse.EnsureSuccessStatusCode();
        var subscription = JsonConvert.DeserializeObject<CardSubscriptionResponse>(
            await unsubscribeResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(subscription);
        Assert.IsFalse(subscription.IsSubscribed);

        using var deleteComment = await Http.DeleteAsync(
            $"/api/v1/cards/{createdCard.Id}/comments/{comment.Id}");
        deleteComment.EnsureSuccessStatusCode();

        using var deleteCard = await Http.DeleteAsync($"/api/v1/cards/{createdCard.Id}");
        deleteCard.EnsureSuccessStatusCode();

        Assert.IsNotNull(Server);
        await using var scope = Server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await db.KanbanCards.FindAsync(createdCard.Id));
        Assert.IsFalse(await db.KanbanCardComments.AnyAsync(item => item.CardId == createdCard.Id));
    }

    [TestMethod]
    public async Task CardApiTransfersToEditableBoardAndReturnsTheReplacementCard()
    {
        await AuthenticateLocalAsync();

        using var createSourceBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = $"Transfer source {Guid.NewGuid():N}" }));
        createSourceBoard.EnsureSuccessStatusCode();
        var sourceBoard = JsonConvert.DeserializeObject<BoardResponse>(
            await createSourceBoard.Content.ReadAsStringAsync())!.Board;

        using var createTargetBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = $"Transfer target {Guid.NewGuid():N}" }));
        createTargetBoard.EnsureSuccessStatusCode();
        var targetBoard = JsonConvert.DeserializeObject<BoardResponse>(
            await createTargetBoard.Content.ReadAsStringAsync())!.Board;

        using var createCard = await Http.PostAsync(
            $"/api/v1/columns/{sourceBoard.Columns.First().Id}/cards",
            Json(new CreateCardRequest { Title = "Move between boards", Description = "Keep card fields" }));
        createCard.EnsureSuccessStatusCode();
        var sourceCard = JsonConvert.DeserializeObject<CardResponse>(
            await createCard.Content.ReadAsStringAsync())!.Card;

        using var addComment = await Http.PostAsync(
            $"/api/v1/cards/{sourceCard.Id}/comments",
            Json(new AddCardCommentRequest { Content = "Source board history" }));
        addComment.EnsureSuccessStatusCode();

        using var targetsResponse = await Http.GetAsync(
            $"/api/v1/cards/{sourceCard.Id}/transfer-targets");
        targetsResponse.EnsureSuccessStatusCode();
        var targets = JsonConvert.DeserializeObject<CardTransferTargetsResponse>(
            await targetsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(targets);
        Assert.IsTrue(targets.Boards.Any(board => board.Id == targetBoard.Id));
        Assert.IsFalse(targets.Boards.Any(board => board.Id == sourceBoard.Id));

        var targetColumn = targetBoard.Columns.First();
        using var transferResponse = await Http.PostAsync(
            $"/api/v1/cards/{sourceCard.Id}/transfer",
            Json(new TransferCardRequest
            {
                TargetBoardId = targetBoard.Id,
                TargetColumnId = targetColumn.Id
            }));
        transferResponse.EnsureSuccessStatusCode();
        var transferred = JsonConvert.DeserializeObject<CardTransferResponse>(
            await transferResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(transferred);
        Assert.AreNotEqual(sourceCard.Id, transferred.CardId);
        Assert.AreEqual(targetBoard.Id, transferred.BoardId);
        Assert.AreEqual(targetColumn.Id, transferred.ColumnId);

        using var transferredDetailsResponse = await Http.GetAsync(
            $"/api/v1/cards/{transferred.CardId}");
        transferredDetailsResponse.EnsureSuccessStatusCode();
        var transferredDetails = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await transferredDetailsResponse.Content.ReadAsStringAsync())!.Card;
        Assert.AreEqual("Move between boards", transferredDetails.Title);
        Assert.AreEqual(targetBoard.Id, transferredDetails.BoardId);
        Assert.IsNull(transferredDetails.AssignedUser);
        Assert.IsEmpty(transferredDetails.Comments);

        Assert.IsNotNull(Server);
        await using var scope = Server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await db.KanbanCards.FindAsync(sourceCard.Id));
        Assert.IsFalse(await db.KanbanCardComments.AnyAsync(comment => comment.CardId == sourceCard.Id));
    }

    [TestMethod]
    public async Task ReadOnlyBoardExposesCardDetailsButRejectsChangesAndReplies()
    {
        await AuthenticateLocalAsync();

        using var createBoard = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Read-only Android details" }));
        createBoard.EnsureSuccessStatusCode();
        var board = JsonConvert.DeserializeObject<BoardResponse>(
            await createBoard.Content.ReadAsStringAsync())!.Board;

        using var createCard = await Http.PostAsync(
            $"/api/v1/columns/{board.Columns.First().Id}/cards",
            Json(new CreateCardRequest { Title = "Visible but protected" }));
        createCard.EnsureSuccessStatusCode();
        var card = JsonConvert.DeserializeObject<CardResponse>(
            await createCard.Content.ReadAsStringAsync())!.Card;

        var viewerEmail = $"readonly-{Guid.NewGuid():N}@example.com";
        const string viewerPassword = "Viewer-password-123!";
        Assert.IsNotNull(Server);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<User>>();
            var viewer = new User
            {
                UserName = viewerEmail,
                Email = viewerEmail,
                DisplayName = "Read-only viewer"
            };
            var created = await userManager.CreateAsync(viewer, viewerPassword);
            Assert.IsTrue(created.Succeeded);
            var db = services.GetRequiredService<TemplateDbContext>();
            db.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                SharedWithUserId = viewer.Id,
                Permission = SharePermission.ReadOnly
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateLocalAsync(viewerEmail, viewerPassword);
        using var detailsResponse = await Http.GetAsync($"/api/v1/cards/{card.Id}");
        detailsResponse.EnsureSuccessStatusCode();
        var details = JsonConvert.DeserializeObject<CardDetailsResponse>(
            await detailsResponse.Content.ReadAsStringAsync())!.Card;
        Assert.IsFalse(details.CanEdit);
        Assert.IsFalse(details.CanDelete);
        Assert.IsEmpty(details.AvailableAssignees);
        Assert.IsEmpty(details.AvailableColumns);

        using var updateResponse = await Http.PutAsync(
            $"/api/v1/cards/{card.Id}",
            Json(new UpdateCardRequest
            {
                Title = "Should not change",
                Priority = nameof(Priority.High)
            }));
        var updateError = JsonConvert.DeserializeObject<AiurResponse>(
            await updateResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(updateError);
        Assert.AreEqual(Code.Unauthorized, updateError.Code);

        using var commentResponse = await Http.PostAsync(
            $"/api/v1/cards/{card.Id}/comments",
            Json(new AddCardCommentRequest { Content = "Should not be accepted" }));
        var commentError = JsonConvert.DeserializeObject<AiurResponse>(
            await commentResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(commentError);
        Assert.AreEqual(Code.Unauthorized, commentError.Code);

        using var labelResponse = await Http.PostAsync(
            $"/api/v1/cards/{card.Id}/labels",
            Json(new AddCardLabelRequest { Name = "Protected" }));
        var labelError = JsonConvert.DeserializeObject<AiurResponse>(
            await labelResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(labelError);
        Assert.AreEqual(Code.Unauthorized, labelError.Code);

        using var transferTargetsResponse = await Http.GetAsync(
            $"/api/v1/cards/{card.Id}/transfer-targets");
        var transferTargetsError = JsonConvert.DeserializeObject<AiurResponse>(
            await transferTargetsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(transferTargetsError);
        Assert.AreEqual(Code.Unauthorized, transferTargetsError.Code);
    }

    [TestMethod]
    public async Task BoardManagementApiUpdatesColumnsSharingAndDeletesThroughProtocol()
    {
        var ownerEmail = $"board-owner-{Guid.NewGuid():N}@example.com";
        var targetEmail = $"board-target-{Guid.NewGuid():N}@example.com";
        const string password = "Board-owner-password-123!";
        string ownerUserId;
        string targetUserId;
        Assert.IsNotNull(Server);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var owner = new User { UserName = ownerEmail, Email = ownerEmail, DisplayName = "Board owner" };
            var target = new User { UserName = targetEmail, Email = targetEmail, DisplayName = "Board target" };
            Assert.IsTrue((await userManager.CreateAsync(owner, password)).Succeeded);
            Assert.IsTrue((await userManager.CreateAsync(target)).Succeeded);
            ownerUserId = owner.Id;
            targetUserId = target.Id;
        }

        await AuthenticateLocalAsync(ownerEmail, password);
        using var createResponse = await Http.PostAsync(
            "/api/v1/boards",
            Json(new CreateBoardRequest { Name = "Mobile settings" }));
        createResponse.EnsureSuccessStatusCode();
        var created = JsonConvert.DeserializeObject<BoardResponse>(
            await createResponse.Content.ReadAsStringAsync())!.Board;

        using var boardUpdateResponse = await Http.PutAsync(
            $"/api/v1/boards/{created.Id}",
            Json(new UpdateBoardRequest { Name = "Mobile roadmap", Order = 55 }));
        boardUpdateResponse.EnsureSuccessStatusCode();
        var updated = JsonConvert.DeserializeObject<BoardResponse>(
            await boardUpdateResponse.Content.ReadAsStringAsync())!.Board;
        Assert.AreEqual("Mobile roadmap", updated.Name);
        Assert.AreEqual(55, updated.Order);

        using var createColumnResponse = await Http.PostAsync(
            $"/api/v1/boards/{created.Id}/columns",
            Json(new CreateColumnRequest { Name = "QA" }));
        createColumnResponse.EnsureSuccessStatusCode();
        var withColumn = JsonConvert.DeserializeObject<BoardResponse>(
            await createColumnResponse.Content.ReadAsStringAsync())!.Board;
        var column = withColumn.Columns.Single(item => item.Name == "QA");

        using var columnUpdateResponse = await Http.PutAsync(
            $"/api/v1/columns/{column.Id}",
            Json(new UpdateColumnRequest { Name = "Verification", Status = nameof(ColumnStatus.InProgress) }));
        columnUpdateResponse.EnsureSuccessStatusCode();
        var columnUpdated = JsonConvert.DeserializeObject<BoardResponse>(
            await columnUpdateResponse.Content.ReadAsStringAsync())!.Board;
        Assert.AreEqual(nameof(ColumnStatus.InProgress),
            columnUpdated.Columns.Single(item => item.Id == column.Id).Status);

        using var moveResponse = await Http.PutAsync(
            $"/api/v1/columns/{column.Id}/position",
            Json(new MoveColumnRequest { NewOrder = 0 }));
        moveResponse.EnsureSuccessStatusCode();
        var moved = JsonConvert.DeserializeObject<BoardResponse>(
            await moveResponse.Content.ReadAsStringAsync())!.Board;
        Assert.AreEqual(column.Id, moved.Columns.First().Id);

        var timelineColumn = moved.Columns.Single(item => item.Status == nameof(ColumnStatus.NotStarted));
        using var createTimelineCardResponse = await Http.PostAsync(
            $"/api/v1/columns/{timelineColumn.Id}/cards",
            Json(new CreateCardRequest { Title = "Native timeline" }));
        createTimelineCardResponse.EnsureSuccessStatusCode();
        var timelineCard = JsonConvert.DeserializeObject<CardResponse>(
            await createTimelineCardResponse.Content.ReadAsStringAsync())!.Card;
        var plannedStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var dueDate = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        using var updateTimelineCardResponse = await Http.PutAsync(
            $"/api/v1/cards/{timelineCard.Id}",
            Json(new UpdateCardRequest
            {
                Title = timelineCard.Title,
                Priority = nameof(Priority.High),
                AssignedUserId = ownerUserId,
                PlannedStartTime = plannedStart,
                DueDate = dueDate
            }));
        updateTimelineCardResponse.EnsureSuccessStatusCode();
        using var timelineCommentResponse = await Http.PostAsync(
            $"/api/v1/cards/{timelineCard.Id}/comments",
            Json(new AddCardCommentRequest { Content = "Visible on the native board" }));
        timelineCommentResponse.EnsureSuccessStatusCode();
        using var timelineLabelResponse = await Http.PostAsync(
            $"/api/v1/cards/{timelineCard.Id}/labels",
            Json(new AddCardLabelRequest { Name = "Mobile" }));
        timelineLabelResponse.EnsureSuccessStatusCode();
        using var enrichedBoardResponse = await Http.GetAsync($"/api/v1/boards/{created.Id}");
        enrichedBoardResponse.EnsureSuccessStatusCode();
        var enrichedBoard = JsonConvert.DeserializeObject<BoardResponse>(
            await enrichedBoardResponse.Content.ReadAsStringAsync())!.Board;
        var enrichedCard = enrichedBoard.Columns
            .SelectMany(item => item.Cards)
            .Single(item => item.Id == timelineCard.Id);
        Assert.AreEqual(nameof(Priority.High), enrichedCard.Priority);
        Assert.IsNotNull(enrichedCard.AssignedUser);
        Assert.AreEqual(1, enrichedCard.CommentCount);
        Assert.AreEqual("Mobile", enrichedCard.Labels.Single().Name);
        using var ganttResponse = await Http.GetAsync($"/api/v1/boards/{created.Id}/gantt");
        ganttResponse.EnsureSuccessStatusCode();
        var gantt = JsonConvert.DeserializeObject<GanttResponse>(await ganttResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(gantt);
        var ganttCard = gantt.Cards.Single(item => item.Id == timelineCard.Id);
        Assert.AreEqual(plannedStart, ganttCard.PlannedStartTime);
        Assert.AreEqual(dueDate, ganttCard.DueDate);
        Assert.AreEqual(nameof(Priority.High), ganttCard.Priority);

        using var visibilityResponse = await Http.PutAsync(
            $"/api/v1/boards/{created.Id}/visibility",
            Json(new UpdateBoardVisibilityRequest { IsPublic = true }));
        visibilityResponse.EnsureSuccessStatusCode();
        var visible = JsonConvert.DeserializeObject<BoardSharingResponse>(
            await visibilityResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(visible);
        Assert.IsTrue(visible.IsPublic);
        Assert.EndsWith($"/{created.Id}", visible.PublicUrl);
        Assert.IsTrue(visible.AvailableUsers.Any(user => user.Id == targetUserId));

        using var addShareResponse = await Http.PostAsync(
            $"/api/v1/boards/{created.Id}/shares",
            Json(new AddBoardShareRequest
            {
                TargetUserId = targetUserId,
                Permission = nameof(SharePermission.Editable)
            }));
        addShareResponse.EnsureSuccessStatusCode();
        var shared = JsonConvert.DeserializeObject<BoardSharingResponse>(
            await addShareResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(shared);
        var share = shared.Shares.Single(item => item.TargetId == targetUserId);
        Assert.AreEqual(nameof(SharePermission.Editable), share.Permission);

        using var removeShareResponse = await Http.DeleteAsync(
            $"/api/v1/boards/{created.Id}/shares/{share.Id}");
        removeShareResponse.EnsureSuccessStatusCode();
        var withoutShare = JsonConvert.DeserializeObject<BoardSharingResponse>(
            await removeShareResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(withoutShare);
        Assert.IsEmpty(withoutShare.Shares);

        using var deleteColumnResponse = await Http.DeleteAsync($"/api/v1/columns/{column.Id}");
        deleteColumnResponse.EnsureSuccessStatusCode();
        var withoutColumn = JsonConvert.DeserializeObject<BoardResponse>(
            await deleteColumnResponse.Content.ReadAsStringAsync())!.Board;
        Assert.IsFalse(withoutColumn.Columns.Any(item => item.Id == column.Id));

        using var deleteBoardResponse = await Http.DeleteAsync($"/api/v1/boards/{created.Id}");
        deleteBoardResponse.EnsureSuccessStatusCode();
        using var missingBoardResponse = await Http.GetAsync($"/api/v1/boards/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, missingBoardResponse.StatusCode);
    }

    [TestMethod]
    public async Task AccountApiUpdatesProfileReportsPasswordAvatarAndDeletesAccount()
    {
        using var anonymous = await Http.GetAsync("/api/v1/account");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var email = $"account-{Guid.NewGuid():N}@example.com";
        const string password = "Account-password-123!";
        const string newPassword = "Account-password-456!";
        string userId;
        Assert.IsNotNull(Server);
        await using (var scope = Server.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = new User { UserName = email, Email = email, DisplayName = "Mobile account" };
            Assert.IsTrue((await userManager.CreateAsync(user, password)).Succeeded);
            await scope.ServiceProvider.GetRequiredService<GlobalSettingsService>()
                .UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "True");
            userId = user.Id;
        }

        await AuthenticateLocalAsync(email, password);
        using var profileResponse = await Http.GetAsync("/api/v1/account");
        profileResponse.EnsureSuccessStatusCode();
        var profile = JsonConvert.DeserializeObject<AccountProfileResponse>(
            await profileResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(profile);
        Assert.AreEqual("Mobile account", profile.DisplayName);
        Assert.IsTrue(profile.CanChangePassword);
        Assert.AreEqual(0, profile.OwnedBoardCount);

        using var updateProfileResponse = await Http.PutAsync(
            "/api/v1/account/profile",
            Json(new UpdateProfileRequest { DisplayName = "Native account" }));
        updateProfileResponse.EnsureSuccessStatusCode();
        var updatedProfile = JsonConvert.DeserializeObject<AccountProfileResponse>(
            await updateProfileResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(updatedProfile);
        Assert.AreEqual("Native account", updatedProfile.DisplayName);

        using var reportSettingsResponse = await Http.PutAsync(
            "/api/v1/account/report-settings",
            Json(new UpdateReportSettingsRequest
            {
                EnableDailyReport = false,
                EnableWeeklyReport = true,
                DailyReportLanguage = "ja"
            }));
        reportSettingsResponse.EnsureSuccessStatusCode();
        var reportSettings = JsonConvert.DeserializeObject<AccountProfileResponse>(
            await reportSettingsResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(reportSettings);
        Assert.IsFalse(reportSettings.EnableDailyReport);
        Assert.IsTrue(reportSettings.EnableWeeklyReport);
        Assert.AreEqual("ja", reportSettings.DailyReportLanguage);

        using var passwordResponse = await Http.PutAsync(
            "/api/v1/account/password",
            Json(new ChangePasswordRequest
            {
                CurrentPassword = password,
                NewPassword = newPassword,
                ConfirmPassword = newPassword
            }));
        passwordResponse.EnsureSuccessStatusCode();

        using var grantResponse = await Http.GetAsync("/api/v1/uploads/avatar");
        grantResponse.EnsureSuccessStatusCode();
        var grant = JsonConvert.DeserializeObject<CardImageUploadGrantResponse>(
            await grantResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(grant);
        CollectionAssert.Contains(grant.AllowedExtensions, "png");
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        using var multipart = new MultipartFormDataContent();
        var image = new ByteArrayContent(pngBytes);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(image, "file", $"account-{Guid.NewGuid():N}.png");
        using var uploadResponse = await Http.PostAsync(grant.UploadUrl, multipart);
        uploadResponse.EnsureSuccessStatusCode();
        var upload = JsonConvert.DeserializeObject<CardImageUploadResponse>(
            await uploadResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(upload);
        Assert.StartsWith("avatar/", upload.Path);

        using var avatarResponse = await Http.PutAsync(
            "/api/v1/account/avatar",
            Json(new UpdateAvatarRequest { AvatarRelativePath = upload.Path }));
        avatarResponse.EnsureSuccessStatusCode();
        var avatar = JsonConvert.DeserializeObject<AccountProfileResponse>(
            await avatarResponse.Content.ReadAsStringAsync());
        Assert.IsNotNull(avatar);
        Assert.AreEqual(upload.Path, avatar.AvatarRelativePath);

        using var deleteResponse = await Http.DeleteAsync("/api/v1/account");
        deleteResponse.EnsureSuccessStatusCode();
        await using var verificationScope = Server.Services.CreateAsyncScope();
        var verificationManager = verificationScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.IsNull(await verificationManager.FindByIdAsync(userId));

        Http.DefaultRequestHeaders.Authorization = null;
        using var oldPasswordLogin = await Http.PostAsync(
            "/api/v1/auth/local/login",
            Json(new LocalLoginRequest { EmailOrUserName = email, Password = password }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        using var newPasswordLogin = await Http.PostAsync(
            "/api/v1/auth/local/login",
            Json(new LocalLoginRequest { EmailOrUserName = email, Password = newPassword }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, newPasswordLogin.StatusCode);
    }

    [TestMethod]
    public async Task MyOperationLogsApiRequiresAuthenticationAndReportsDisabledStorage()
    {
        using var anonymousResponse = await Http.GetAsync("/api/v1/audit-logs/mine");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        await AuthenticateLocalAsync();
        using var response = await Http.GetAsync("/api/v1/audit-logs/mine?page=3");
        response.EnsureSuccessStatusCode();
        var model = JsonConvert.DeserializeObject<OperationLogListResponse>(
            await response.Content.ReadAsStringAsync());

        Assert.IsNotNull(model);
        Assert.IsFalse(model.Enabled);
        Assert.AreEqual(3, model.CurrentPage);
        Assert.AreEqual(1, model.TotalPages);
        Assert.AreEqual(0, model.TotalCount);
        Assert.IsEmpty(model.Logs);
        Assert.AreEqual(0, (int)model.Code);
    }

    private static T AssertProtocol<T>(IActionResult result) where T : class
    {
        var json = result as JsonResult;
        Assert.IsNotNull(json);
        var value = json.Value as T;
        Assert.IsNotNull(value);
        return value;
    }

    private async Task AuthenticateLocalAsync(
        string identity = "admin@default.com",
        string password = "Admin@123456!")
    {
        using var login = await Http.PostAsync(
            "/api/v1/auth/local/login",
            Json(new LocalLoginRequest
            {
                EmailOrUserName = identity,
                Password = password
            }));
        login.EnsureSuccessStatusCode();
        var authentication = JsonConvert.DeserializeObject<LocalAuthenticationResponse>(
            await login.Content.ReadAsStringAsync());
        Assert.IsNotNull(authentication);
        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
    }

    private static StringContent Json(object value) =>
        new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
}
