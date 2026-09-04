using System.Net;
using System.Text;
using Aiursoft.AiurProtocol.Exceptions;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;

namespace Aiursoft.Kanban.Tests.Sdk;

[TestClass]
public sealed class KanbanApiClientTests
{
    [TestMethod]
    public async Task GetBoardUsesConfiguredEndpointAndBearerToken()
    {
        var handler = new RecordingHandler("""
            {"code":0,"message":"ok","protocolVersion":"10.0.30","board":{"id":42,"name":"Mobile","columns":[]}}
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetBoardAsync(42);

        Assert.AreEqual(42, result.Board.Id);
        Assert.AreEqual("https://kanban.example/api/v1/boards/42", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("access-token", handler.AuthorizationParameter);
    }

    [TestMethod]
    public async Task ArchivedBoardsDeserializeAndArchiveStateUsesScopedPutRoute()
    {
        var listHandler = new RecordingHandler("""
            {
              "code":0,
              "message":"archived",
              "protocolVersion":"10.0.30",
              "ownedBoards":[{"id":3,"name":"Legacy","isOwner":true,"cardCount":8,"archivedTime":"2026-09-03T12:00:00Z"}],
              "sharedBoards":[{"id":4,"name":"Shared legacy","isOwner":false,"permission":"ReadOnly","sharedVia":"Direct share"}]
            }
            """);
        await using (var provider = BuildProvider(listHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>().GetArchivedBoardsAsync();
            Assert.AreEqual("Legacy", result.OwnedBoards.Single().Name);
            Assert.AreEqual(8, result.OwnedBoards.Single().CardCount);
            Assert.AreEqual("Direct share", result.SharedBoards.Single().SharedVia);
            Assert.AreEqual(
                "https://kanban.example/api/v1/boards/archived",
                listHandler.RequestUri?.ToString());
        }

        var updateHandler = new RecordingHandler("""
            {"code":2,"message":"archived","protocolVersion":"10.0.30","boardId":3,"isArchived":true,"archivedTime":"2026-09-03T12:00:00Z"}
            """);
        await using (var provider = BuildProvider(updateHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>()
                .SetBoardArchivedAsync(3, archive: true);
            Assert.IsTrue(result.IsArchived);
            Assert.AreEqual(HttpMethod.Put, updateHandler.Method);
            Assert.AreEqual(
                "https://kanban.example/api/v1/boards/3/archive",
                updateHandler.RequestUri?.ToString());
            StringAssert.Contains(updateHandler.Body ?? string.Empty, "archive");
        }
    }

    [TestMethod]
    public async Task CreateCardPostsJsonThroughAiurProtocol()
    {
        var handler = new RecordingHandler("""
            {"code":2,"message":"created","protocolVersion":"10.0.30","card":{"id":9,"columnId":3,"title":"Ship Android","order":0}}
            """);
        await using var provider = BuildProvider(handler, "token");

        var result = await provider.GetRequiredService<KanbanApiClient>().CreateCardAsync(3, new CreateCardRequest
        {
            Title = "Ship Android",
            Description = "Native .NET"
        });

        Assert.AreEqual(9, result.Card.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("application/json", handler.ContentType);
        StringAssert.Contains(handler.Body ?? string.Empty, "Ship Android");
    }

    [TestMethod]
    public async Task InvalidRequestIsRejectedBeforeNetworkCall()
    {
        var handler = new RecordingHandler("{}");
        await using var provider = BuildProvider(handler, "token");

        await Assert.ThrowsAsync<AiurBadApiInputException>(() =>
            provider.GetRequiredService<KanbanApiClient>().CreateCardAsync(1, new CreateCardRequest()));
        Assert.AreEqual(0, handler.CallCount);
    }

    [TestMethod]
    public async Task GetCardDetailsDeserializesEditingContext()
    {
        var handler = new RecordingHandler("""
            {
              "code":0,
              "message":"loaded",
              "protocolVersion":"10.0.30",
              "card":{
                "id":17,
                "boardId":2,
                "boardName":"Product",
                "columnId":3,
                "columnName":"Doing",
                "title":"Android details",
                "priority":"High",
                "canEdit":true,
                "canDelete":true,
                "availableAssignees":[{"id":"user-1","displayName":"Ada"}],
                "availableColumns":[{"id":3,"name":"Doing"},{"id":4,"name":"Done"}],
                "availableLabels":[{"id":4,"name":"Mobile","color":"#3B82F6"}],
                "comments":[{
                  "id":8,
                  "content":"Looks good",
                  "images":"",
                  "author":{"id":"user-1","displayName":"Ada"},
                  "creationTime":"2026-09-03T10:00:00Z",
                  "canDelete":true
                }]
              }
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetCardDetailsAsync(17);

        Assert.AreEqual("Android details", result.Card.Title);
        Assert.IsTrue(result.Card.CanEdit);
        Assert.AreEqual("Ada", result.Card.AvailableAssignees.Single().DisplayName);
        Assert.AreEqual("Done", result.Card.AvailableColumns.Last().Name);
        Assert.AreEqual("Mobile", result.Card.AvailableLabels.Single().Name);
        Assert.AreEqual("Looks good", result.Card.Comments.Single().Content);
        Assert.AreEqual("https://kanban.example/api/v1/cards/17", handler.RequestUri?.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Method);
    }

    [TestMethod]
    public async Task UpdateCardUsesPutAndSerializesEditableFields()
    {
        var handler = new RecordingHandler("""
            {"code":2,"message":"updated","protocolVersion":"10.0.30","card":{"id":17,"title":"Ready","priority":"Urgent"}}
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().UpdateCardAsync(17, new UpdateCardRequest
        {
            Title = "Ready",
            Description = "Ship it",
            Priority = "Urgent",
            AssignedUserId = "user-1",
            DueDate = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.AreEqual("Urgent", result.Card.Priority);
        Assert.AreEqual(HttpMethod.Put, handler.Method);
        Assert.AreEqual("application/json", handler.ContentType);
        StringAssert.Contains(handler.Body ?? string.Empty, "Ship it");
        StringAssert.Contains(handler.Body ?? string.Empty, "user-1");
    }

    [TestMethod]
    public async Task DeleteCommentUsesScopedDeleteRoute()
    {
        var handler = new RecordingHandler("""
            {"code":2,"message":"deleted","protocolVersion":"10.0.30"}
            """);
        await using var provider = BuildProvider(handler, "access-token");

        await provider.GetRequiredService<KanbanApiClient>().DeleteCardCommentAsync(17, 8);

        Assert.AreEqual(HttpMethod.Delete, handler.Method);
        Assert.AreEqual("https://kanban.example/api/v1/cards/17/comments/8", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task EmptyCommentIsRejectedBeforeNetworkCall()
    {
        var handler = new RecordingHandler("{}");
        await using var provider = BuildProvider(handler, "access-token");

        await Assert.ThrowsAsync<AiurBadApiInputException>(() =>
            provider.GetRequiredService<KanbanApiClient>().AddCardCommentAsync(17, new AddCardCommentRequest()));

        Assert.AreEqual(0, handler.CallCount);
    }

    [TestMethod]
    public async Task CardLabelsUseScopedJsonAndDeleteRoutes()
    {
        var addHandler = new RecordingHandler("""
            {"code":2,"message":"added","protocolVersion":"10.0.30","label":{"id":4,"name":"Mobile","color":"#3B82F6"}}
            """);
        await using (var provider = BuildProvider(addHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>()
                .AddCardLabelAsync(17, new AddCardLabelRequest { Name = "Mobile" });
            Assert.AreEqual(4, result.Label.Id);
            Assert.AreEqual(HttpMethod.Post, addHandler.Method);
            Assert.AreEqual("https://kanban.example/api/v1/cards/17/labels", addHandler.RequestUri?.ToString());
            StringAssert.Contains(addHandler.Body ?? string.Empty, "Mobile");
        }

        var removeHandler = new RecordingHandler("""
            {"code":2,"message":"removed","protocolVersion":"10.0.30"}
            """);
        await using (var provider = BuildProvider(removeHandler, "access-token"))
        {
            await provider.GetRequiredService<KanbanApiClient>().RemoveCardLabelAsync(17, 4);
            Assert.AreEqual(HttpMethod.Delete, removeHandler.Method);
            Assert.AreEqual("https://kanban.example/api/v1/cards/17/labels/4", removeHandler.RequestUri?.ToString());
        }
    }

    [TestMethod]
    public async Task CardTransferUsesScopedTargetsAndJsonMutationRoutes()
    {
        var targetsHandler = new RecordingHandler("""
            {
              "code":0,
              "message":"targets",
              "protocolVersion":"10.0.30",
              "boards":[{"id":9,"name":"Operations","columns":[{"id":12,"name":"Inbox"}]}]
            }
            """);
        await using (var provider = BuildProvider(targetsHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>()
                .GetCardTransferTargetsAsync(17);
            Assert.AreEqual("Operations", result.Boards.Single().Name);
            Assert.AreEqual(12, result.Boards.Single().Columns.Single().Id);
            Assert.AreEqual(
                "https://kanban.example/api/v1/cards/17/transfer-targets",
                targetsHandler.RequestUri?.ToString());
        }

        var transferHandler = new RecordingHandler("""
            {"code":2,"message":"transferred","protocolVersion":"10.0.30","cardId":81,"boardId":9,"columnId":12}
            """);
        await using (var provider = BuildProvider(transferHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>()
                .TransferCardAsync(17, new TransferCardRequest
                {
                    TargetBoardId = 9,
                    TargetColumnId = 12
                });
            Assert.AreEqual(81, result.CardId);
            Assert.AreEqual(HttpMethod.Post, transferHandler.Method);
            Assert.AreEqual(
                "https://kanban.example/api/v1/cards/17/transfer",
                transferHandler.RequestUri?.ToString());
            StringAssert.Contains(transferHandler.Body ?? string.Empty, "targetBoardId");
        }
    }

    [TestMethod]
    public async Task GetDailyReportsUsesAiurEndpointQueryAndDeserializesReportMetadata()
    {
        var reportId = Guid.NewGuid();
        var handler = new RecordingHandler($$"""
            {
              "code":0,
              "message":"daily reports",
              "protocolVersion":"10.0.30",
              "reports":[{
                "id":"{{reportId}}",
                "reportType":"Plan",
                "content":"Focus on Android parity.",
                "date":"2026-09-03T00:00:00Z",
                "generatedAt":"2026-09-03T01:00:00Z"
              }],
              "currentPage":2,
              "totalPages":4,
              "totalCount":34
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetDailyReportsAsync(2);

        Assert.AreEqual(reportId, result.Reports.Single().Id);
        Assert.AreEqual("Plan", result.Reports.Single().ReportType);
        Assert.AreEqual(2, result.CurrentPage);
        Assert.AreEqual(4, result.TotalPages);
        Assert.AreEqual("https://kanban.example/api/v1/reports/daily?page=2", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task GetWeeklyReportUsesGuidRouteAndDeserializesContent()
    {
        var reportId = Guid.NewGuid();
        var handler = new RecordingHandler($$"""
            {
              "code":0,
              "message":"weekly report loaded",
              "protocolVersion":"10.0.30",
              "report":{
                "id":"{{reportId}}",
                "content":"Completed native report views.",
                "weekStart":"2026-08-31T00:00:00Z",
                "generatedAt":"2026-09-03T01:00:00Z"
              }
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetWeeklyReportAsync(reportId);

        Assert.AreEqual("Completed native report views.", result.Report.Content);
        Assert.AreEqual(
            $"https://kanban.example/api/v1/reports/weekly/{reportId}",
            handler.RequestUri?.ToString());
        Assert.AreEqual(HttpMethod.Get, handler.Method);
    }

    [TestMethod]
    public async Task ReportGenerationAndDiscardUseAuthenticatedAiurRoutes()
    {
        var dailyId = Guid.NewGuid();
        var weeklyId = Guid.NewGuid();
        var handler = new RecordingHandler(
            $$"""
            {
              "code":2,
              "message":"daily generated",
              "protocolVersion":"10.0.30",
              "report":{
                "id":"{{dailyId}}",
                "reportType":"Plan",
                "content":"Plan",
                "date":"2026-09-03T00:00:00Z",
                "generatedAt":"2026-09-03T01:00:00Z"
              }
            }
            """,
            $$"""
            {
              "code":2,
              "message":"weekly generated",
              "protocolVersion":"10.0.30",
              "report":{
                "id":"{{weeklyId}}",
                "content":"Week",
                "weekStart":"2026-08-31T00:00:00Z",
                "generatedAt":"2026-09-03T01:00:00Z"
              }
            }
            """,
            """
            {"code":2,"message":"discarded","protocolVersion":"10.0.30"}
            """);
        await using var provider = BuildProvider(handler, "access-token");
        var client = provider.GetRequiredService<KanbanApiClient>();

        var daily = await client.GenerateDailyReportAsync("plan");
        Assert.AreEqual(dailyId, daily.Report.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual(
            "https://kanban.example/api/v1/reports/daily/plan/generate",
            handler.RequestUri?.ToString());

        var weekly = await client.GenerateWeeklyReportAsync();
        Assert.AreEqual(weeklyId, weekly.Report.Id);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual(
            "https://kanban.example/api/v1/reports/weekly/generate",
            handler.RequestUri?.ToString());

        await client.DeleteWeeklyReportAsync(weeklyId);
        Assert.AreEqual(HttpMethod.Delete, handler.Method);
        Assert.AreEqual(
            $"https://kanban.example/api/v1/reports/weekly/{weeklyId}",
            handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task GetMyTasksUsesFiltersAndDeserializesCardContext()
    {
        var handler = new RecordingHandler("""
            {
              "code":0,
              "message":"assigned tasks",
              "protocolVersion":"10.0.30",
              "cards":[{
                "id":21,
                "boardId":3,
                "boardName":"Product",
                "columnId":5,
                "columnName":"Doing",
                "status":"InProgress",
                "title":"Complete Android parity",
                "priority":"Urgent",
                "labels":[{"id":8,"name":"Mobile","color":"#2563EB"}]
              }],
              "targetUser":{"id":"user-1","displayName":"Ada"},
              "selectedLabelIds":[8],
              "selectedStatus":"in-progress",
              "selectedLabelMode":"all",
              "selectedSort":"priority-desc"
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetMyTasksAsync(
            "user-1",
            "in-progress",
            [8],
            "all",
            "priority-desc");

        Assert.AreEqual("Complete Android parity", result.Cards.Single().Title);
        Assert.AreEqual("Mobile", result.Cards.Single().Labels.Single().Name);
        Assert.AreEqual("Ada", result.TargetUser.DisplayName);
        Assert.AreEqual(HttpMethod.Get, handler.Method);
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        StringAssert.Contains(handler.RequestUri?.Query ?? string.Empty, "targetuserid=user-1");
        StringAssert.Contains(handler.RequestUri?.Query ?? string.Empty, "status=in-progress");
        StringAssert.Contains(handler.RequestUri?.Query ?? string.Empty, "labelids=8");
        StringAssert.Contains(handler.RequestUri?.Query ?? string.Empty, "labelmode=all");
        StringAssert.Contains(handler.RequestUri?.Query ?? string.Empty, "sort=priority-desc");
    }

    [TestMethod]
    public async Task SearchCardsEncodesQueryAndDeserializesAiSearchMetadata()
    {
        var handler = new RecordingHandler("""
            {
              "code":0,
              "message":"AI card search results",
              "protocolVersion":"10.0.30",
              "query":"Android parity",
              "usedAi":true,
              "totalCount":1,
              "cards":[{
                "id":31,
                "boardId":3,
                "boardName":"Product",
                "columnId":5,
                "columnName":"Doing",
                "status":"InProgress",
                "title":"Complete Android parity",
                "priority":"High",
                "labels":[]
              }]
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>()
            .SearchCardsAsync("Android parity");

        Assert.IsTrue(result.UsedAi);
        Assert.AreEqual(1, result.TotalCount);
        Assert.AreEqual(31, result.Cards.Single().Id);
        Assert.AreEqual(
            "https://kanban.example/api/v1/search/cards?query=Android parity",
            handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task GetDashboardDeserializesCountsBoardsTasksAndReports()
    {
        var reportId = Guid.NewGuid();
        var handler = new RecordingHandler($$"""
            {
              "code":0,
              "message":"dashboard",
              "protocolVersion":"10.0.30",
              "ownedBoardCount":2,
              "sharedBoardCount":1,
              "assignedTaskCount":3,
              "overdueTaskCount":1,
              "inProgressTaskCount":2,
              "assignedTasks":[{"id":41,"title":"Dashboard task","labels":[]}],
              "ownedBoards":[{"boardId":7,"name":"Product","totalCards":5}],
              "sharedBoards":[],
              "latestPlan":{
                "id":"{{reportId}}",
                "reportType":"Plan",
                "content":"Ship the dashboard.",
                "date":"2026-09-03T00:00:00Z",
                "generatedAt":"2026-09-03T01:00:00Z"
              }
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetDashboardAsync();

        Assert.AreEqual(2, result.OwnedBoardCount);
        Assert.AreEqual(3, result.AssignedTaskCount);
        Assert.AreEqual(41, result.AssignedTasks.Single().Id);
        Assert.AreEqual("Product", result.OwnedBoards.Single().Name);
        Assert.AreEqual(reportId, result.LatestPlan?.Id);
        Assert.AreEqual("https://kanban.example/api/v1/dashboard", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task NotificationsDeserializeAndMarkReadThroughScopedRoutes()
    {
        var listHandler = new RecordingHandler("""
            {
              "code":0,
              "message":"notifications",
              "protocolVersion":"10.0.30",
              "unreadCount":1,
              "notifications":[{
                "id":51,
                "cardId":41,
                "boardId":7,
                "type":"CommentAdded",
                "message":"Ada commented on a card",
                "actorUserName":"Ada",
                "creationTime":"2026-09-03T01:00:00Z"
              }]
            }
            """);
        await using (var provider = BuildProvider(listHandler, "access-token"))
        {
            var result = await provider.GetRequiredService<KanbanApiClient>().GetNotificationsAsync();
            Assert.AreEqual(1, result.UnreadCount);
            Assert.AreEqual("Ada", result.Notifications.Single().ActorUserName);
            Assert.AreEqual("https://kanban.example/api/v1/notifications", listHandler.RequestUri?.ToString());
        }

        var updateHandler = new RecordingHandler("""
            {"code":2,"message":"read","protocolVersion":"10.0.30"}
            """);
        await using (var provider = BuildProvider(updateHandler, "access-token"))
        {
            await provider.GetRequiredService<KanbanApiClient>().MarkNotificationReadAsync(51);
            Assert.AreEqual(HttpMethod.Put, updateHandler.Method);
            Assert.AreEqual(
                "https://kanban.example/api/v1/notifications/51/read",
                updateHandler.RequestUri?.ToString());
        }

        var updateAllHandler = new RecordingHandler("""
            {"code":2,"message":"all read","protocolVersion":"10.0.30"}
            """);
        await using (var provider = BuildProvider(updateAllHandler, "access-token"))
        {
            await provider.GetRequiredService<KanbanApiClient>().MarkAllNotificationsReadAsync();
            Assert.AreEqual(HttpMethod.Put, updateAllHandler.Method);
            Assert.AreEqual(
                "https://kanban.example/api/v1/notifications/read-all",
                updateAllHandler.RequestUri?.ToString());
        }
    }

    [TestMethod]
    public async Task MyOperationLogsUsePagedAuthenticatedAiurRoute()
    {
        var handler = new RecordingHandler("""
            {
              "code":0,
              "message":"logs",
              "protocolVersion":"10.0.30",
              "currentPage":3,
              "totalPages":4,
              "totalCount":151,
              "enabled":true,
              "logs":[{
                "eventTime":"2026-09-03T01:00:00Z",
                "action":"UpdateCard",
                "category":"Kanban",
                "summary":"Updated card Mobile parity",
                "source":"API",
                "ipAddress":"127.0.0.1",
                "traceId":"trace-1"
              }]
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>()
            .GetMyOperationLogsAsync(3);

        Assert.IsTrue(result.Enabled);
        Assert.AreEqual(151, result.TotalCount);
        Assert.AreEqual("Updated card Mobile parity", result.Logs.Single().Summary);
        Assert.AreEqual("Kanban", result.Logs.Single().Category);
        Assert.AreEqual(
            "https://kanban.example/api/v1/audit-logs/mine?page=3",
            handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("access-token", handler.AuthorizationParameter);
    }

    [TestMethod]
    public async Task CardImagesUseAiurGrantMultipartUploadAndBoundedThumbnailDownload()
    {
        var handler = new RecordingHandler(
            """
            {
              "code":0,
              "message":"grant",
              "protocolVersion":"10.0.30",
              "uploadUrl":"/upload/kanban-images?token=signed-token",
              "maxSizeInMb":10,
              "allowedExtensions":["png","jpg"]
            }
            """,
            """
            {
              "path":"kanban-images/phone.png",
              "internetPath":"https://kanban.example/download/kanban-images/phone.png"
            }
            """,
            "thumbnail-bytes");
        await using var provider = BuildProvider(handler, "access-token");
        var client = provider.GetRequiredService<KanbanApiClient>();

        var grant = await client.GetCardImageUploadGrantAsync();
        await using var image = new MemoryStream([1, 2, 3]);
        var uploaded = await client.UploadCardImageAsync(grant, image, "phone.png", "image/png");

        Assert.AreEqual(2, handler.CallCount);
        Assert.AreEqual("https://kanban.example/download/kanban-images/phone.png", uploaded.InternetPath);
        Assert.AreEqual("https://kanban.example/upload/kanban-images?token=signed-token", handler.RequestUri?.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("multipart/form-data", handler.ContentType);
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("access-token", handler.AuthorizationParameter);
        StringAssert.Contains(handler.Body ?? string.Empty, "phone.png");
        StringAssert.Contains(handler.Body ?? string.Empty, "image/png");

        var thumbnail = await client.DownloadCardImageThumbnailAsync(uploaded.InternetPath);

        Assert.AreEqual("thumbnail-bytes", Encoding.UTF8.GetString(thumbnail));
        Assert.AreEqual(3, handler.CallCount);
        Assert.AreEqual(
            "https://kanban.example/download/kanban-images/phone.png?w=320",
            handler.RequestUri?.ToString());
        Assert.IsNull(handler.AuthorizationScheme, "Bearer credentials must not leak to arbitrary image hosts.");
    }

    [TestMethod]
    public async Task AgentConversationRoutesUseAiurProtocolAndDeserializeAdvice()
    {
        var conversationId = Guid.NewGuid();
        var adviceId = Guid.NewGuid();
        var handler = new RecordingHandler(
            $$"""
            {
              "code":2,
              "message":"started",
              "protocolVersion":"10.0.30",
              "conversationId":"{{conversationId}}"
            }
            """,
            $$"""
            {
              "code":0,
              "message":"status",
              "protocolVersion":"10.0.30",
              "conversationId":"{{conversationId}}",
              "boardId":7,
              "state":"AwaitingApproval",
              "messages":[
                {"role":"user","content":"Create a release card","toolCalls":[]},
                {"role":"assistant","content":"I found the right board.","toolCalls":[]}
              ],
              "pendingAdvice":[{
                "adviceId":"{{adviceId}}",
                "toolDisplayName":"Create card",
                "parameterDisplay":"Release mobile app",
                "status":"Pending",
                "parameters":[{"key":"title","displayKey":"Title","value":"Release mobile app"}],
                "resolvedName":"Product / Backlog"
              }]
            }
            """,
            """
            {"code":2,"message":"rejected","protocolVersion":"10.0.30"}
            """);
        await using var provider = BuildProvider(handler, "access-token");
        var client = provider.GetRequiredService<KanbanApiClient>();

        var started = await client.SendAgentMessageAsync(new AgentSendMessageRequest
        {
            BoardId = 7,
            Message = "Create a release card"
        });
        Assert.AreEqual(conversationId, started.ConversationId);
        Assert.AreEqual("https://kanban.example/api/v1/agent/messages", handler.RequestUri?.ToString());
        StringAssert.Contains(handler.Body ?? string.Empty, "Create a release card");

        var status = await client.GetAgentStatusAsync(conversationId);
        Assert.AreEqual("AwaitingApproval", status.State);
        Assert.AreEqual("I found the right board.", status.Messages.Last().Content);
        Assert.AreEqual("Create card", status.PendingAdvice.Single().ToolDisplayName);
        Assert.AreEqual("Release mobile app", status.PendingAdvice.Single().Parameters.Single().Value);

        await client.RejectAgentAdviceAsync(conversationId, adviceId);
        Assert.AreEqual(
            $"https://kanban.example/api/v1/agent/conversations/{conversationId}/advice/{adviceId}/reject",
            handler.RequestUri?.ToString());
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task AgentExcelConversionPostsMultipartWithBearerToken()
    {
        var handler = new RecordingHandler("""
            {
              "code":2,
              "message":"converted",
              "protocolVersion":"10.0.30",
              "markdown":"| Task | Owner |",
              "fileName":"planning.xlsx"
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");
        await using var workbook = new MemoryStream([1, 2, 3, 4]);

        var result = await provider.GetRequiredService<KanbanApiClient>()
            .ConvertAgentExcelAsync(workbook, "planning.xlsx");

        Assert.AreEqual("| Task | Owner |", result.Markdown);
        Assert.AreEqual("planning.xlsx", result.FileName);
        Assert.AreEqual("https://kanban.example/api/v1/agent/excel", handler.RequestUri?.ToString());
        Assert.AreEqual("multipart/form-data", handler.ContentType);
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        StringAssert.Contains(handler.Body ?? string.Empty, "planning.xlsx");
        StringAssert.Contains(handler.Body ?? string.Empty,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [TestMethod]
    public async Task BoardAndColumnManagementUseScopedAiurProtocolRoutes()
    {
        var handler = new RecordingHandler(
            """
            {"code":2,"message":"updated","protocolVersion":"10.0.30","board":{"id":7,"name":"Roadmap","order":40,"columns":[]}}
            """,
            """
            {"code":2,"message":"updated","protocolVersion":"10.0.30","board":{"id":7,"name":"Roadmap","columns":[{"id":9,"name":"Review","status":"InProgress","order":0,"cards":[]}]}}
            """,
            """
            {"code":2,"message":"moved","protocolVersion":"10.0.30","board":{"id":7,"name":"Roadmap","columns":[{"id":9,"name":"Review","status":"InProgress","order":0,"cards":[]}]}}
            """,
            """
            {"code":2,"message":"deleted","protocolVersion":"10.0.30","board":{"id":7,"name":"Roadmap","columns":[]}}
            """);
        await using var provider = BuildProvider(handler, "access-token");
        var client = provider.GetRequiredService<KanbanApiClient>();

        var board = await client.UpdateBoardAsync(7, new UpdateBoardRequest { Name = "Roadmap", Order = 40 });
        Assert.AreEqual(40, board.Board.Order);
        Assert.AreEqual(HttpMethod.Put, handler.Method);
        Assert.AreEqual("https://kanban.example/api/v1/boards/7", handler.RequestUri?.ToString());
        StringAssert.Contains(handler.Body ?? string.Empty, "Roadmap");

        var column = await client.UpdateColumnAsync(9, new UpdateColumnRequest
        {
            Name = "Review",
            Status = "InProgress"
        });
        Assert.AreEqual("InProgress", column.Board.Columns.Single().Status);
        Assert.AreEqual("https://kanban.example/api/v1/columns/9", handler.RequestUri?.ToString());

        await client.MoveColumnAsync(9, 0);
        Assert.AreEqual("https://kanban.example/api/v1/columns/9/position", handler.RequestUri?.ToString());
        StringAssert.Contains(handler.Body ?? string.Empty, "newOrder");

        await client.DeleteColumnAsync(9);
        Assert.AreEqual(HttpMethod.Delete, handler.Method);
        Assert.AreEqual("https://kanban.example/api/v1/columns/9", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task BoardSharingRoutesDeserializeTargetsAndShares()
    {
        var shareId = Guid.NewGuid();
        var handler = new RecordingHandler($$"""
            {
              "code":0,
              "message":"sharing",
              "protocolVersion":"10.0.30",
              "boardId":7,
              "boardName":"Roadmap",
              "isPublic":false,
              "publicUrl":"https://kanban.example/PublicKanban/View?boardId=7",
              "shares":[{"id":"{{shareId}}","targetId":"user-2","targetName":"Ada","targetType":"User","permission":"Editable"}],
              "availableUsers":[{"id":"user-2","name":"Ada"}],
              "availableRoles":[{"id":"role-1","name":"Editors"}]
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetBoardSharingAsync(7);

        Assert.AreEqual("Ada", result.AvailableUsers.Single().Name);
        Assert.AreEqual(shareId, result.Shares.Single().Id);
        Assert.AreEqual("Editable", result.Shares.Single().Permission);
        Assert.AreEqual("https://kanban.example/api/v1/boards/7/sharing", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task GanttRouteDeserializesPlannedAndActualDates()
    {
        var handler = new RecordingHandler("""
            {
              "code":0,
              "message":"gantt",
              "protocolVersion":"10.0.30",
              "boardId":7,
              "boardName":"Roadmap",
              "cards":[{
                "id":12,
                "boardId":7,
                "boardName":"Roadmap",
                "columnId":3,
                "columnName":"Doing",
                "status":"InProgress",
                "title":"Native timeline",
                "priority":"High",
                "plannedStartTime":"2026-09-01T00:00:00Z",
                "dueDate":"2026-09-05T00:00:00Z",
                "actualStartTime":"2026-09-02T08:00:00Z"
              }]
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");

        var result = await provider.GetRequiredService<KanbanApiClient>().GetGanttAsync(7);

        Assert.AreEqual("Roadmap", result.BoardName);
        Assert.AreEqual(new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            result.Cards.Single().DueDate);
        Assert.AreEqual("https://kanban.example/api/v1/boards/7/gantt", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    [TestMethod]
    public async Task AccountProfileAndReportSettingsUseAiurProtocolRoutes()
    {
        var handler = new RecordingHandler(
            """
            {
              "code":0,"message":"profile","protocolVersion":"10.0.30",
              "displayName":"Ada","email":"ada@example.com","avatarUrl":"https://kanban.example/download/avatar/a.png",
              "avatarRelativePath":"avatar/a.png","canChangeDisplayName":true,"canChangePassword":true,
              "enableDailyReport":true,"enableWeeklyReport":false,"dailyReportLanguage":"en","ownedBoardCount":2
            }
            """,
            """
            {
              "code":0,"message":"saved","protocolVersion":"10.0.30",
              "displayName":"Ada","email":"ada@example.com","enableDailyReport":false,
              "enableWeeklyReport":true,"dailyReportLanguage":"ja","ownedBoardCount":2
            }
            """);
        await using var provider = BuildProvider(handler, "access-token");
        var client = provider.GetRequiredService<KanbanApiClient>();

        var profile = await client.GetAccountProfileAsync();
        Assert.AreEqual("Ada", profile.DisplayName);
        Assert.AreEqual(2, profile.OwnedBoardCount);
        Assert.AreEqual("https://kanban.example/api/v1/account", handler.RequestUri?.ToString());

        var settings = await client.UpdateReportSettingsAsync(new UpdateReportSettingsRequest
        {
            EnableDailyReport = false,
            EnableWeeklyReport = true,
            DailyReportLanguage = "ja"
        });
        Assert.AreEqual("ja", settings.DailyReportLanguage);
        Assert.AreEqual(HttpMethod.Put, handler.Method);
        Assert.AreEqual("https://kanban.example/api/v1/account/report-settings", handler.RequestUri?.ToString());
        StringAssert.Contains(handler.Body ?? string.Empty, "ja");
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
    }

    private static ServiceProvider BuildProvider(RecordingHandler handler, string? token)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKanbanSdk("https://kanban.example/");
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
        services.AddScoped<IKanbanAccessTokenProvider>(_ => new StubTokenProvider(token));
        return services.BuildServiceProvider();
    }

    private sealed class StubTokenProvider(string? token) : IKanbanAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(token);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(params string[] responseBodies) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var responseBody = responseBodies[Math.Min(CallCount - 1, responseBodies.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
