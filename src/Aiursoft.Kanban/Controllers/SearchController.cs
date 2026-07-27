using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.SearchViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
public class SearchController(
    TemplateDbContext db,
    UserManager<User> userManager,
    CardVectorSearchService vectorSearchService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Search",
        CascadedLinksIcon = "search",
        CascadedLinksOrder = 4,
        LinkText = "Global Search",
        LinkOrder = 1)]
    public async Task<IActionResult> Index([FromQuery] string? q)
    {
        var model = new SearchResultViewModel
        {
            Query = q ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(q))
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User)!;
        
        // 1. Determine which boards the user has read access to
        var ownedBoardIds = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Select(b => b.Id)
            .ToListAsync();

        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var sharedBoardIds = await db.BoardShares
            .Where(share => share.SharedWithUserId == userId ||
                            (share.SharedWithRoleId != null && userRoleIds.Contains(share.SharedWithRoleId)))
            .Select(share => share.BoardId)
            .ToListAsync();

        var publicBoardIds = await db.KanbanBoards
            .Where(b => b.IsPublic)
            .Select(b => b.Id)
            .ToListAsync();

        var allowedBoardIds = ownedBoardIds
            .Union(sharedBoardIds)
            .Union(publicBoardIds)
            .Distinct()
            .ToList();

        // 2. Base query
        var baseQuery = db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .Include(c => c.CardLabels)
                .ThenInclude(l => l.Label)
            .Where(c => allowedBoardIds.Contains(c.Column.BoardId));

        // 3. Try Vector Search first
        var (usedAi, aiResults, totalCount) = await vectorSearchService.SearchAsync(
            baseQuery,
            q,
            allowedBoardIds,
            page: 1,
            pageSize: 20);

        if (usedAi)
        {
            model.UsedAi = true;
            model.Cards = aiResults;
            model.TotalCount = totalCount;
        }
        else
        {
            // 4. Fallback to Keyword Search
            var normalizedQ = q.ToUpper();
            var keywordQuery = baseQuery
                .Where(c => c.Title.ToUpper().Contains(normalizedQ) || 
                            (c.Description != null && c.Description.ToUpper().Contains(normalizedQ)));

            model.UsedAi = false;
            model.TotalCount = await keywordQuery.CountAsync();
            model.Cards = await keywordQuery
                .OrderByDescending(c => c.CreationTime)
                .Take(20)
                .ToListAsync();
        }

        return this.StackView(model);
    }
}
