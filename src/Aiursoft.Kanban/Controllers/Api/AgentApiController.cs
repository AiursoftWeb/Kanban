using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/agent")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class AgentApiController(
    IAgentService agentService,
    AdviceService adviceService,
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanApiAccessService access,
    MarkItDownService markItDownService) : ControllerBase
{
    private const long MaxExcelBytes = 10L * 1024 * 1024;

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] AgentSendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return this.Protocol(Code.InvalidInput, "Message is required.");
        }

        var userId = CurrentUserId();
        if (request.ConversationId.HasValue)
        {
            var continued = agentService.ContinueRun(
                request.ConversationId.Value,
                userId,
                request.Message,
                request.ExcelMarkdown);
            if (continued == null)
            {
                return this.Protocol(
                    Code.InvalidInput,
                    "Conversation not found, not yours, or still processing.");
            }
            return ConversationResult(continued.Value, "Conversation continued.");
        }

        if (request.BoardId > 0)
        {
            var board = await db.KanbanBoards.FindAsync(request.BoardId);
            if (board == null)
            {
                return this.Protocol(Code.NotFound, "Board not found.");
            }
            if (!await access.CanReadAsync(board, userId))
            {
                return this.Protocol(Code.Unauthorized, "You cannot access this board.");
            }
        }

        var conversationId = await agentService.StartRun(
            userId,
            request.BoardId,
            request.Message,
            request.ExcelMarkdown);
        return ConversationResult(conversationId, "Conversation started.");
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public IActionResult Status(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null)
        {
            return this.Protocol(Code.NotFound, "Conversation not found.");
        }
        if (conversation.UserId != CurrentUserId())
        {
            return this.Protocol(Code.Unauthorized, "This conversation belongs to another user.");
        }

        var pendingAdvice = adviceService.GetPendingForConversation(conversationId);
        return this.Protocol(new AgentStatusResponse
        {
            Code = Code.ResultShown,
            Message = "Conversation status.",
            ConversationId = conversation.Id,
            BoardId = conversation.BoardId,
            State = conversation.State.ToString(),
            Messages = conversation.Messages
                .Where(message => message.Role != "system" && !message.IsMeta)
                .Select(message => new AgentMessageDto
                {
                    Role = message.Role ?? "unknown",
                    Content = message.Content,
                    ToolCallId = message.ToolCallId,
                    ToolCalls = message.ToolCalls?.Select(call => new AgentToolCallDto
                    {
                        Id = call.Id,
                        Name = call.Function?.Name,
                        Arguments = call.Function?.Arguments
                    }).ToList() ?? []
                }).ToList(),
            PendingAdvice = pendingAdvice.Select(advice => new AgentAdviceDto
            {
                AdviceId = advice.Id,
                ToolDisplayName = advice.ToolDisplayName,
                ParameterDisplay = advice.ParameterDisplay,
                Status = advice.Status.ToString(),
                Parameters = advice.DisplayParameters.Select(parameter => new AgentAdviceParameterDto
                {
                    Key = parameter.Key,
                    DisplayKey = parameter.DisplayKey,
                    Value = parameter.Value
                }).ToList(),
                ResolvedName = advice.ResolvedName
            }).ToList(),
            ErrorMessage = conversation.ErrorMessage
        });
    }

    [HttpPost("conversations/{conversationId:guid}/advice/{adviceId:guid}/approve")]
    public IActionResult ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        var ownershipError = ValidateOwnedConversation(conversationId);
        if (ownershipError != null)
        {
            return ownershipError;
        }
        var advice = adviceService.Get(adviceId);
        if (advice == null || advice.ConversationId != conversationId)
        {
            return this.Protocol(Code.NotFound, "Proposed action not found.");
        }
        if (advice.Status != AdviceStatus.Pending)
        {
            return this.Protocol(Code.NoActionTaken, "The proposed action was already resolved.");
        }

        agentService.ApproveAdvice(conversationId, adviceId);
        return this.Protocol(Code.JobDone, "Proposed action approved.");
    }

    [HttpPost("conversations/{conversationId:guid}/advice/{adviceId:guid}/reject")]
    public IActionResult RejectAdvice(Guid conversationId, Guid adviceId)
    {
        var ownershipError = ValidateOwnedConversation(conversationId);
        if (ownershipError != null)
        {
            return ownershipError;
        }
        var advice = adviceService.Get(adviceId);
        if (advice == null || advice.ConversationId != conversationId)
        {
            return this.Protocol(Code.NotFound, "Proposed action not found.");
        }
        if (advice.Status != AdviceStatus.Pending)
        {
            return this.Protocol(Code.NoActionTaken, "The proposed action was already resolved.");
        }

        agentService.RejectAdvice(conversationId, adviceId);
        return this.Protocol(Code.JobDone, "Proposed action rejected.");
    }

    [HttpPost("conversations/{conversationId:guid}/approve-all")]
    public IActionResult ApproveAll(Guid conversationId)
    {
        var ownershipError = ValidateOwnedConversation(conversationId);
        if (ownershipError != null)
        {
            return ownershipError;
        }
        var count = adviceService.GetPendingForConversation(conversationId).Count;
        if (count == 0)
        {
            return this.Protocol(Code.NoActionTaken, "There are no pending actions.");
        }

        agentService.ApproveAll(conversationId);
        return this.Protocol(Code.JobDone, $"Approved {count} proposed action(s).");
    }

    [HttpPost("conversations/{conversationId:guid}/cancel")]
    public IActionResult Cancel(Guid conversationId)
    {
        var ownershipError = ValidateOwnedConversation(conversationId);
        if (ownershipError != null)
        {
            return ownershipError;
        }

        agentService.CancelRun(conversationId);
        return this.Protocol(Code.JobDone, "Conversation cancelled.");
    }

    [HttpPost("excel")]
    [RequestSizeLimit(MaxExcelBytes + 64 * 1024)]
    public async Task<IActionResult> ConvertExcel([FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return this.Protocol(Code.InvalidInput, "Select an Excel workbook first.");
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return this.Protocol(Code.InvalidInput, "Only .xlsx files are supported.");
        }
        if (file.Length > MaxExcelBytes)
        {
            return this.Protocol(Code.InvalidInput, "The Excel workbook cannot exceed 10 MB.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var markdown = await markItDownService.ConvertExcelToMarkdownAsync(
                stream,
                Path.GetFileName(file.FileName),
                HttpContext.RequestAborted);
            return this.Protocol(new AgentExcelConversionResponse
            {
                Code = Code.JobDone,
                Message = "Excel workbook converted.",
                Markdown = markdown,
                FileName = Path.GetFileName(file.FileName)
            });
        }
        catch (Exception exception)
        {
            return this.Protocol(Code.RemoteNotAccessible, $"Excel conversion failed: {exception.Message}");
        }
    }

    private IActionResult? ValidateOwnedConversation(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null)
        {
            return this.Protocol(Code.NotFound, "Conversation not found.");
        }
        return conversation.UserId == CurrentUserId()
            ? null
            : this.Protocol(Code.Unauthorized, "This conversation belongs to another user.");
    }

    private IActionResult ConversationResult(Guid conversationId, string message) =>
        this.Protocol(new AgentConversationResponse
        {
            Code = Code.JobDone,
            Message = message,
            ConversationId = conversationId
        });

    private string CurrentUserId() => userManager.GetUserId(User)!;
}
