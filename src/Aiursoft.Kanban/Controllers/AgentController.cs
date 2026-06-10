using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.AgentViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[LimitPerMin]
[Authorize]
public class AgentController(
    IAgentService agentService,
    AdviceService adviceService,
    KanbanAccessService access,
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [HttpGet]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "Overview Kanban",
        CascadedLinksIcon = "bot",
        CascadedLinksOrder = 2,
        LinkText = "AI Assistant",
        LinkOrder = 3)]
    public async Task<IActionResult> Index(int? boardId)
    {
        var userId = userManager.GetUserId(User)!;

        // Load boards the user owns or has access to
        var ownedBoards = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Order)
            .ToListAsync();

        KanbanBoard? currentBoard = null;
        if (boardId.HasValue)
        {
            currentBoard = await db.KanbanBoards
                .Include(b => b.Columns)
                .FirstOrDefaultAsync(b => b.Id == boardId.Value);

            if (currentBoard != null && !await access.HasReadAccess(currentBoard, userId))
                return Forbid();
        }

        return this.StackView(new AgentIndexViewModel
        {
            CurrentBoard = currentBoard,
            UserBoards = ownedBoards
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { Error = "Message is required." });

        var userId = userManager.GetUserId(User)!;

        // Continue existing conversation
        if (request.ConversationId.HasValue)
        {
            var conversationId = agentService.ContinueRun(
                request.ConversationId.Value, userId, request.Message);
            if (conversationId == null)
                return BadRequest(new { Error = "Conversation not found, not yours, or still processing." });
            return Ok(new { ConversationId = conversationId.Value });
        }

        // Start new conversation
        if (request.BoardId > 0)
        {
            var board = await db.KanbanBoards.FindAsync(request.BoardId);
            if (board == null)
                return NotFound(new { Error = "Board not found." });

            if (!await access.HasReadAccess(board, userId))
                return Forbid();
        }

        var newConversationId = agentService.StartRun(userId, request.BoardId, request.Message);
        return Ok(new { ConversationId = newConversationId });
    }

    [HttpGet]
    public IActionResult Status(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null)
            return NotFound(new { Error = "Conversation not found." });

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId)
            return Forbid();

        var messages = conversation.Messages
            .Where(m => m.Role != "system" && !m.IsMeta)
            .Select(m => new ChatMessageViewModel
            {
                Role = m.Role ?? "unknown",
                Content = m.Content,
                ToolCalls = m.ToolCalls?.Select(tc => new ToolCallViewModel
                {
                    Id = tc.Id,
                    Name = tc.Function?.Name,
                    Arguments = tc.Function?.Arguments
                }).ToList(),
                ToolCallId = m.ToolCallId,
                IsMeta = m.IsMeta
            }).ToList();

        // Annotate tool_call messages with their advice status
        var pendingAdvice = adviceService.GetPendingForConversation(conversationId);
        foreach (var msg in messages.Where(m => m.ToolCalls?.Count > 0))
        {
            if (msg.ToolCalls == null) continue;
            foreach (var tc in msg.ToolCalls)
            {
                var matchingAdvice = pendingAdvice.FirstOrDefault(a => a.ToolCallId == tc.Id);
                if (matchingAdvice != null)
                {
                    msg.AdviceId = matchingAdvice.Id;
                    msg.AdviceStatus = matchingAdvice.Status.ToString();
                }
            }
        }

        var adviceViewModels = pendingAdvice.Select(a => new AdviceViewModel
        {
            AdviceId = a.Id,
            ToolDisplayName = a.ToolDisplayName,
            ParameterDisplay = a.ParameterDisplay,
            Status = a.Status.ToString()
        }).ToList();

        return Ok(new AgentStatusViewModel
        {
            ConversationId = conversation.Id,
            State = conversation.State.ToString(),
            Messages = messages,
            PendingAdvice = adviceViewModels,
            ErrorMessage = conversation.ErrorMessage
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.ApproveAdvice(conversationId, adviceId);
        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RejectAdvice(Guid conversationId, Guid adviceId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.RejectAdvice(conversationId, adviceId);
        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApproveAll(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.ApproveAll(conversationId);
        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.CancelRun(conversationId);
        return Ok(new { success = true });
    }
}
