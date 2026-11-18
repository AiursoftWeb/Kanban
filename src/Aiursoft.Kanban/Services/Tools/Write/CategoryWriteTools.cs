using System.ComponentModel;
using System.Data;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Write;

[McpServerToolType]
public class CategoryWriteTools(
    TemplateDbContext db,
    KanbanAccessService access,
    CurrentUserService currentUser,
    IMediator mediator,
    ILogger<CategoryWriteTools> logger) : IScopedDependency
{
    private async Task<bool> CanEditAsync(KanbanBoard board) =>
        !board.IsArchived && await access.HasEditAccess(board, currentUser.UserId);

    [McpServerTool, Description("Create a category on a board")]
    [Advice]
    public async Task<string> CreateCategory(
        [Description("Board ID")] int boardId,
        [Description("Category name")] string name)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return "Error: Board not found.";
        if (!await CanEditAsync(board)) return "Error: You do not have permission to edit this board.";
        if (string.IsNullOrWhiteSpace(name)) return "Error: Category name is required.";
        name = name.Trim();
        if (name.Length > 100) return "Error: Category name must contain 1 to 100 characters.";
        if (await db.KanbanCategories.AnyAsync(c => c.BoardId == boardId && c.Name == name))
            return "Error: A category with this name already exists.";
        var order = await db.KanbanCategories.Where(c => c.BoardId == boardId).MaxAsync(c => (int?)c.Order) ?? 0;
        var category = new KanbanCategory { BoardId = boardId, Name = name, Order = order + 1 };
        db.KanbanCategories.Add(category);
        await db.SaveChangesAsync();
        return $"Category created: #{category.Id} \"{category.Name}\".";
    }

    [McpServerTool, Description("Rename a category")]
    [Advice]
    public async Task<string> RenameCategory(
        [Description("Category ID")] int categoryId,
        [Description("New category name")] string name)
    {
        var category = await db.KanbanCategories.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category == null) return "Error: Category not found.";
        if (!await CanEditAsync(category.Board)) return "Error: You do not have permission to edit this board.";
        if (string.IsNullOrWhiteSpace(name)) return "Error: Category name is required.";
        name = name.Trim();
        if (name.Length > 100) return "Error: Category name must contain 1 to 100 characters.";
        if (await db.KanbanCategories.AnyAsync(c => c.BoardId == category.BoardId && c.Id != categoryId && c.Name == name))
            return "Error: A category with this name already exists.";
        category.Name = name;
        await db.SaveChangesAsync();
        return $"Category #{categoryId} renamed to \"{category.Name}\".";
    }

    [McpServerTool, Description("Delete an empty, non-default category")]
    [Advice]
    public async Task<string> DeleteCategory([Description("Category ID")] int categoryId)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;
            var category = await db.KanbanCategories.Include(c => c.Board).FirstOrDefaultAsync(c => c.Id == categoryId);
            if (category == null) return "Error: Category not found.";
            if (!await CanEditAsync(category.Board)) return "Error: You do not have permission to edit this board.";
            if (category.IsDefault) return "Error: The default category cannot be deleted.";
            if (await db.KanbanCards.AnyAsync(c => c.CategoryId == categoryId))
                return "Error: Move all cards to another category before deleting this category.";
            db.KanbanCategories.Remove(category);
            await db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return $"Category #{categoryId} deleted.";
        });
    }

    [McpServerTool, Description("Set a card's category on the same board, placing it at the end of that category. Preserves status column, status order, dates, and recurrence. Use the default category ID to return to default; categories cannot be null.")]
    [Advice]
    public async Task<string> SetCardCategory(
        [Description("Card ID")] int cardId,
        [Description("Target category ID")] int categoryId)
    {
        var userId = currentUser.UserId;
        var card = await db.KanbanCards.Include(c => c.Column).ThenInclude(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return "Error: Card not found.";
        if (!await CanEditAsync(card.Column.Board)) return "Error: You do not have permission to edit this board.";
        if (!await db.KanbanCategories.AnyAsync(c => c.Id == categoryId && c.BoardId == card.Column.BoardId))
            return "Error: Category does not belong to this board.";

        var changed = card.CategoryId != categoryId;
        var cards = await db.KanbanCards.Where(c => c.CategoryId == categoryId && c.Id != cardId)
            .OrderBy(c => c.CategoryOrder).ThenBy(c => c.Id).ToListAsync();
        cards.Add(card);
        card.CategoryId = categoryId;
        for (var i = 0; i < cards.Count; i++) cards[i].CategoryOrder = i;
        await db.SaveChangesAsync();
        if (changed)
        {
            try { await mediator.Publish(new CardUpdatedEvent(cardId, userId, ["category"])); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to publish category change for card {CardId}.", cardId); }
        }
        return $"Card #{cardId} moved to category #{categoryId} at category order {card.CategoryOrder}.";
    }
}
