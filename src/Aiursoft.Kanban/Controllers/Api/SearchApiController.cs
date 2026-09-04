using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.SDK.Models;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers.Api;

[Route("api/v1/search")]
[Authorize(AuthenticationSchemes = LocalApiAuthenticationDefaults.ApiSchemes)]
[ApiExceptionHandler(PassthroughRemoteErrors = true, PassthroughAiurServerException = true)]
[ApiModelStateChecker]
public sealed class SearchApiController(
    TemplateDbContext db,
    UserManager<User> userManager,
    CardVectorSearchService vectorSearchService) : ControllerBase
{
    private const int ResultLimit = 20;

    [HttpGet("cards")]
    public async Task<IActionResult> Cards([FromQuery] string? query)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return this.Protocol(new CardSearchResponse
            {
                Code = Code.ResultShown,
                Message = "Enter a search query.",
                Query = string.Empty
            });
        }

        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The authenticated token is not linked to a local user.");
        var roleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        var ownedBoardIds = await db.KanbanBoards
            .Where(board => board.UserId == userId)
            .Select(board => board.Id)
            .ToListAsync();
        var sharedBoardIds = await db.BoardShares
            .Where(share => share.SharedWithUserId == userId ||
                (share.SharedWithRoleId != null && roleIds.Contains(share.SharedWithRoleId)))
            .Select(share => share.BoardId)
            .ToListAsync();
        var publicBoardIds = await db.KanbanBoards
            .Where(board => board.IsPublic)
            .Select(board => board.Id)
            .ToListAsync();
        var allowedBoardIds = ownedBoardIds
            .Union(sharedBoardIds)
            .Union(publicBoardIds)
            .Distinct()
            .ToList();

        var baseQuery = db.KanbanCards
            .Include(card => card.Column)
                .ThenInclude(column => column.Board)
            .Include(card => card.CardLabels)
                .ThenInclude(link => link.Label)
            .Include(card => card.AssignedUser)
            .Where(card => allowedBoardIds.Contains(card.Column.BoardId));
        var (usedAi, results, totalCount) = await vectorSearchService.SearchAsync(
            baseQuery,
            normalizedQuery,
            allowedBoardIds,
            page: 1,
            pageSize: ResultLimit,
            ct: HttpContext.RequestAborted);

        if (!usedAi)
        {
            var upperQuery = normalizedQuery.ToUpper();
            var keywordQuery = baseQuery.Where(card =>
                card.Title.ToUpper().Contains(upperQuery) ||
                (card.Description != null && card.Description.ToUpper().Contains(upperQuery)));
            totalCount = await keywordQuery.CountAsync();
            results = await keywordQuery
                .OrderByDescending(card => card.CreationTime)
                .Take(ResultLimit)
                .ToListAsync();
        }

        return this.Protocol(new CardSearchResponse
        {
            Code = Code.ResultShown,
            Message = usedAi ? "AI card search results." : "Card search results.",
            Query = normalizedQuery,
            UsedAi = usedAi,
            TotalCount = totalCount,
            Cards = results.Select(MobileApiMapper.ToTaskDto).ToList()
        });
    }
}
