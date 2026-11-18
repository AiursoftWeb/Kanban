using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class CategoriesController(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanApiAccessService access,
    IMediator mediator,
    ILogger<CategoriesController> logger) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Create(int boardId, string? name)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return NotFound();
        if (!await access.CanEditAsync(board, userManager.GetUserId(User)!)) return Forbid();
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100) return BadRequest("Category name must contain 1 to 100 characters.");
        if (await db.KanbanCategories.AnyAsync(c => c.BoardId == boardId && c.Name == name))
            return BadRequest("A category with this name already exists.");
        var order = await db.KanbanCategories.Where(c => c.BoardId == boardId).MaxAsync(c => (int?)c.Order) ?? 0;
        var category = new KanbanCategory { Name = name, BoardId = boardId, Order = order + 1 };
        db.KanbanCategories.Add(category);
        await db.SaveChangesAsync();
        return Ok(new { category.Id, category.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Rename(int categoryId, string? name)
    {
        var category = await db.KanbanCategories.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null) return NotFound();
        if (!await access.CanEditAsync(category.Board, userManager.GetUserId(User)!)) return Forbid();
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100) return BadRequest("Category name must contain 1 to 100 characters.");
        if (await db.KanbanCategories.AnyAsync(c => c.BoardId == category.BoardId && c.Name == name && c.Id != categoryId))
            return BadRequest("A category with this name already exists.");
        category.Name = name;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int categoryId)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
                : null;
            var category = await db.KanbanCategories.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == categoryId);
            if (category == null) return NotFound();
            if (!await access.CanEditAsync(category.Board, userManager.GetUserId(User)!)) return Forbid();
            if (category.IsDefault) return BadRequest("The default category cannot be deleted.");
            if (await db.KanbanCards.AnyAsync(c => c.CategoryId == categoryId))
                return BadRequest("Move all cards to another category before deleting this category.");
            db.KanbanCategories.Remove(category);
            await db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return Ok();
        });
    }

    [HttpPost]
    public async Task<IActionResult> MoveCard(int cardId, int categoryId, int newOrder)
    {
        var card = await db.KanbanCards.Include(c => c.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();
        var userId = userManager.GetUserId(User)!;
        if (!await access.CanEditAsync(card.Column.Board, userId)) return Forbid();
        if (!await db.KanbanCategories.AnyAsync(c => c.Id == categoryId && c.BoardId == card.Column.BoardId))
            return BadRequest("Category does not belong to this board.");
        var changed = card.CategoryId != categoryId;
        var cards = await db.KanbanCards.Where(c => c.CategoryId == categoryId && c.Id != cardId)
            .OrderBy(c => c.CategoryOrder).ThenBy(c => c.Id).ToListAsync();
        cards.Insert(Math.Clamp(newOrder, 0, cards.Count), card);
        card.CategoryId = categoryId;
        for (var i = 0; i < cards.Count; i++) cards[i].CategoryOrder = i;
        await db.SaveChangesAsync();
        if (changed)
        {
            try { await mediator.Publish(new CardUpdatedEvent(cardId, userId, ["category"])); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to publish category change for card {CardId}.", cardId); }
        }
        return Ok(new { card.Id, card.CategoryId, card.CategoryOrder });
    }
}
