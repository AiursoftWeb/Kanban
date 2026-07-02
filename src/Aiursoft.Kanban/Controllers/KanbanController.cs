using System.Text.RegularExpressions;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Events;
using Aiursoft.Kanban.Models.KanbanViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.Kanban.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Controllers;

[LimitPerMin]
[Authorize]
public class KanbanController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storage,
    IAuthorizationService authorizationService,
    IMediator mediator,
    ILogger<KanbanController> logger) : Controller
{
    private static readonly string[] LabelColors =
    [
        "#EF4444",
        "#F97316",
        "#EAB308",
        "#22C55E",
        "#3B82F6",
        "#8B5CF6",
        "#EC4899",
        "#14B8A6"
    ];

    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "Overview Kanban",
        CascadedLinksIcon = "columns-3",
        CascadedLinksOrder = 2,
        LinkText = "My Created",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(int? boardId)
    {
        var userId = userManager.GetUserId(User)!;

        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Include(b => b.Columns)
                .ThenInclude(c => c.Cards)
            .OrderBy(b => b.Order)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var summaries = new Dictionary<int, BoardSummary>();
        foreach (var board in boards)
        {
            var cards = board.Columns.SelectMany(c => c.Cards).ToList();
            summaries[board.Id] = new BoardSummary
            {
                BoardId = board.Id,
                TotalIncomplete = cards.Count(c => c.Column.ColumnStatus != ColumnStatus.Completed),
                TotalInProgress = cards.Count(c => c.Column.ColumnStatus == ColumnStatus.InProgress),
                TotalCompleted = cards.Count(c => c.Column.ColumnStatus == ColumnStatus.Completed),
                TotalOverdue = cards.Count(c => c.DueDate.HasValue && c.DueDate.Value < now && c.Column.ColumnStatus != ColumnStatus.Completed),
                TotalUnassigned = cards.Count(c => string.IsNullOrEmpty(c.AssignedUserId))
            };
        }

        KanbanBoard? currentBoard = null;
        var canEditCurrentBoard = false;
        if (boardId.HasValue)
        {
            currentBoard = await LoadBoardAsync(boardId.Value);
            if (currentBoard != null)
            {
                if (!await HasReadAccess(currentBoard, userId))
                {
                    currentBoard = null;
                }
                else
                {
                    canEditCurrentBoard = await HasEditAccess(currentBoard, userId);
                }
            }
        }

        return this.StackView(new IndexViewModel
        {
            Boards = boards,
            BoardSummaries = summaries,
            CurrentBoard = currentBoard,
            IsOwner = currentBoard == null || currentBoard.UserId == userId,
            CanEditCurrentBoard = currentBoard == null || canEditCurrentBoard
        });
    }

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "Overview Kanban",
        CascadedLinksIcon = "columns-3",
        CascadedLinksOrder = 2,
        LinkText = "Shared with Me",
        LinkOrder = 2)]
    public async Task<IActionResult> SharedWithMe()
    {
        var userId = userManager.GetUserId(User)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var userRoles = await userManager.GetRolesAsync(user);
        var userRoleIds = await db.Roles
            .Where(r => userRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        var shares = await db.BoardShares
            .Include(s => s.Board)
                .ThenInclude(b => b.Columns)
                    .ThenInclude(c => c.Cards)
            .Where(s => s.SharedWithUserId == userId ||
                        (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync();

        var roleIds = shares
            .Where(s => s.SharedWithRoleId != null)
            .Select(s => s.SharedWithRoleId!)
            .Distinct()
            .ToList();

        var roleNames = await db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name ?? r.Id);

        var now = DateTime.UtcNow;
        var summaries = new Dictionary<int, BoardSummary>();
        foreach (var share in shares)
        {
            var board = share.Board;
            var cards = board.Columns.SelectMany(c => c.Cards).ToList();
            summaries[board.Id] = new BoardSummary
            {
                BoardId = board.Id,
                TotalIncomplete = cards.Count(c => c.Column.ColumnStatus != ColumnStatus.Completed),
                TotalInProgress = cards.Count(c => c.Column.ColumnStatus == ColumnStatus.InProgress),
                TotalCompleted = cards.Count(c => c.Column.ColumnStatus == ColumnStatus.Completed),
                TotalOverdue = cards.Count(c => c.DueDate.HasValue && c.DueDate.Value < now && c.Column.ColumnStatus != ColumnStatus.Completed),
                TotalUnassigned = cards.Count(c => string.IsNullOrEmpty(c.AssignedUserId))
            };
        }

        return this.StackView(new SharedWithMeViewModel
        {
            Shares = shares,
            RoleNames = roleNames,
            BoardSummaries = summaries
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBoard(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        var maxOrder = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .MaxAsync(b => (int?)b.Order) ?? 0;
        var board = new KanbanBoard { Name = name.Trim(), UserId = userId, Order = maxOrder + 100 };
        db.KanbanBoards.Add(board);

        var defaultColumns = new[]
        {
            new KanbanColumn { Name = "To Do", Order = 0, Board = board, ColumnStatus = ColumnStatus.NotStarted },
            new KanbanColumn { Name = "In Progress", Order = 1, Board = board, ColumnStatus = ColumnStatus.InProgress },
            new KanbanColumn { Name = "Done", Order = 2, Board = board, ColumnStatus = ColumnStatus.Completed }
        };
        db.KanbanColumns.AddRange(defaultColumns);

        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new BoardCreatedEvent(
            BoardId: board.Id,
            BoardName: board.Name,
            ActorUserId: userId));
        return RedirectToAction(nameof(Index), new { boardId = board.Id });
    }

    [HttpPost]
    public async Task<IActionResult> CreateColumn(int boardId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Column name is required.");

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(board, userId)) return Forbid();

        var maxOrder = await db.KanbanColumns
            .Where(c => c.BoardId == boardId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        var column = new KanbanColumn
        {
            Name = name.Trim(),
            Order = maxOrder + 1,
            BoardId = boardId
        };
        db.KanbanColumns.Add(column);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new ColumnCreatedEvent(
            ColumnId: column.Id,
            ColumnName: column.Name,
            BoardId: boardId,
            ActorUserId: userId));
        return Ok(new { column.Id, column.Name, column.Order, ColumnStatus = (int)column.ColumnStatus });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCard(int columnId, string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Title is required.");

        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return NotFound("Column not found.");

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == columnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;

        var card = new KanbanCard
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            Order = maxOrder + 1,
            ColumnId = columnId,
            CreatorUserId = userId,
            AssignedUserId = userId
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new CardCreatedEvent(
            CardId: card.Id,
            CardTitle: card.Title,
            ColumnId: columnId,
            BoardId: column.BoardId,
            ActorUserId: userId));
        var creator = await userManager.FindByIdAsync(userId);

        return Ok(new
        {
            card.Id,
            card.Title,
            card.Description,
            card.Order,
            card.ColumnId,
            CreationTime = card.CreationTime.ToString("yyyy-MM-ddTHH:mm"),
            CreatorUserName = GetUserDisplayName(creator),
            CreatorUserInitial = GetUserInitial(creator),
            CreatorUserAvatarUrl = GetUserAvatarUrl(creator)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCard(int cardId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var cardLabels = await db.KanbanCardLabels
            .Where(link => link.CardId == cardId)
            .ToListAsync();
        var comments = await db.KanbanCardComments
            .Where(comment => comment.CardId == cardId)
            .ToListAsync();

        db.KanbanCardLabels.RemoveRange(cardLabels);
        db.KanbanCardComments.RemoveRange(comments);
        db.KanbanCards.Remove(card);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new CardDeletedEvent(
            CardId: cardId,
            CardTitle: card.Title,
            BoardId: card.Column.BoardId,
            ActorUserId: userId));
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> GetTransferTargets(int cardId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var userRoleIds = await db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToListAsync();
        var boards = await db.KanbanBoards
            .Where(board => board.Id != card.Column.BoardId &&
                (board.UserId == userId ||
                 board.BoardShares.Any(share =>
                     share.Permission == SharePermission.Editable &&
                     (share.SharedWithUserId == userId ||
                      (share.SharedWithRoleId != null && userRoleIds.Contains(share.SharedWithRoleId))))))
            .Include(board => board.Columns)
            .OrderBy(board => board.Name)
            .ToListAsync();

        return Ok(boards.Select(board => new
        {
            board.Id,
            board.Name,
            Columns = board.Columns
                .OrderBy(column => column.Order)
                .Select(column => new { column.Id, column.Name })
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferCard(int cardId, int targetBoardId, int targetColumnId)
    {
        var card = await db.KanbanCards
            .Include(c => c.CardLabels)
            .Include(c => c.Column)
                .ThenInclude(column => column.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var targetColumn = await db.KanbanColumns
            .Include(column => column.Board)
            .FirstOrDefaultAsync(column => column.Id == targetColumnId && column.BoardId == targetBoardId);
        if (targetColumn == null) return NotFound();
        if (targetBoardId == card.Column.BoardId)
            return BadRequest("Target board must be different from the source board.");

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();
        if (!await HasEditAccess(targetColumn.Board, userId)) return Forbid();

        var sourceBoardName = card.Column.Board.Name;
        var sourceColumnName = card.Column.Name;

        var maxOrder = await db.KanbanCards
            .Where(c => c.ColumnId == targetColumnId)
            .MaxAsync(c => (int?)c.Order) ?? -1;
        var originalCreatorUserId = card.CreatorUserId;
        var originalAssigneeUserId = card.AssignedUserId;
        var comments = await db.KanbanCardComments
            .Where(comment => comment.CardId == cardId)
            .ToListAsync();
        var transferredCard = new KanbanCard
        {
            Title = card.Title,
            Description = card.Description,
            Order = maxOrder + 1,
            ColumnId = targetColumnId,
            Priority = card.Priority,
            CreatorUserId = card.CreatorUserId ?? userId,
            AssignedUserId = null,
            PlannedStartTime = card.PlannedStartTime,
            DueDate = card.DueDate,
            RecurrenceInterval = card.RecurrenceInterval,
            RecurrenceUnit = card.RecurrenceUnit
        };

        db.KanbanCards.Add(transferredCard);
        db.KanbanCardLabels.AddRange(card.CardLabels.Select(link => new KanbanCardLabel
        {
            Card = transferredCard,
            LabelId = link.LabelId
        }));
        db.KanbanCardComments.RemoveRange(comments);
        db.KanbanCards.Remove(card);
        await db.SaveChangesAsync();

        await PublishOperationEventAsync(new CardTransferredEvent(
            CardId: transferredCard.Id,
            ActorUserId: userId,
            TargetBoardId: targetBoardId,
            OriginalCardId: cardId,
            SourceBoardName: sourceBoardName,
            SourceColumnName: sourceColumnName,
            OriginalCreatorUserId: originalCreatorUserId,
            OriginalAssigneeUserId: originalAssigneeUserId));

        return Ok(new
        {
            transferredCard.Id,
            transferredCard.ColumnId,
            BoardId = targetBoardId
        });
    }

    [HttpPost]
    public async Task<IActionResult> MoveCard(int cardId, int targetColumnId, int newOrder)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == targetColumnId);
        if (column == null) return NotFound();
        if (column.BoardId != card.Column.BoardId)
            return BadRequest("Target column must belong to the same board.");

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var fromColumnId = card.ColumnId;
        var fromColumnName = card.Column.Name;

        var now = DateTime.UtcNow;
        var wasCompleted = card.Column.ColumnStatus == ColumnStatus.Completed;
        switch (column.ColumnStatus)
        {
            case ColumnStatus.InProgress:
                card.ActualStartTime ??= now;
                card.ActualEndTime = null;
                break;
            case ColumnStatus.Completed:
                card.ActualStartTime ??= now;
                card.ActualEndTime = now;
                break;
        }

        var shouldRecur =
            column.ColumnStatus == ColumnStatus.Completed
            && !wasCompleted
            && card.RecurrenceInterval is > 0
            && card.RecurrenceUnit != RecurrenceUnit.None;

        KanbanColumn? recurrenceTargetColumn = null;
        if (shouldRecur)
        {
            var baseline = card.DueDate ?? now;
            card.DueDate = AdvanceByRecurrence(baseline, card.RecurrenceInterval!.Value, card.RecurrenceUnit);

            // 同步推进计划开始时间，保持任务的时间范围一致
            if (card.PlannedStartTime.HasValue)
            {
                card.PlannedStartTime = AdvanceByRecurrence(
                    card.PlannedStartTime.Value,
                    card.RecurrenceInterval!.Value,
                    card.RecurrenceUnit);
            }

            recurrenceTargetColumn = await db.KanbanColumns
                .Where(c => c.BoardId == column.BoardId && c.ColumnStatus == ColumnStatus.NotStarted)
                .OrderBy(c => c.Order)
                .FirstOrDefaultAsync();

            if (recurrenceTargetColumn == null)
            {
                // No NotStarted column on the board; fall back to staying in Completed.
                shouldRecur = false;
            }
            else
            {
                // Reset time tracking so the next cycle starts fresh.
                card.ActualStartTime = null;
                card.ActualEndTime = null;
            }
        }

        var cardsInColumn = await db.KanbanCards
            .Where(c => c.ColumnId == targetColumnId && c.Id != cardId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        card.ColumnId = targetColumnId;

        var allCards = new List<KanbanCard>();
        var idx = 0;
        foreach (var existingCard in cardsInColumn)
        {
            if (idx == newOrder) allCards.Add(card);
            allCards.Add(existingCard);
            idx++;
        }

        if (idx <= newOrder) allCards.Add(card);

        for (var i = 0; i < allCards.Count; i++)
            allCards[i].Order = i;

        if (shouldRecur && recurrenceTargetColumn != null)
        {
            // Re-target the recurring card to the first NotStarted column and
            // append it to the end of that column's order.
            card.ColumnId = recurrenceTargetColumn.Id;
            var destCards = await db.KanbanCards
                .Where(c => c.ColumnId == recurrenceTargetColumn.Id && c.Id != cardId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            for (var i = 0; i < destCards.Count; i++)
                destCards[i].Order = i;
            card.Order = destCards.Count;

            // Resequence the (now ex-)target column to close the gap left by
            // the card that was momentarily placed there.
            var sourceCards = await db.KanbanCards
                .Where(c => c.ColumnId == targetColumnId && c.Id != cardId)
                .OrderBy(c => c.Order)
                .ToListAsync();
            for (var i = 0; i < sourceCards.Count; i++)
                sourceCards[i].Order = i;
        }

        await db.SaveChangesAsync();

        var movedToColumnId = card.ColumnId;
        var movedToColumnName = shouldRecur && recurrenceTargetColumn != null
            ? recurrenceTargetColumn.Name
            : column.Name;
        if (fromColumnId != movedToColumnId && !shouldRecur)
        {
            await PublishOperationEventAsync(new CardMovedEvent(
                CardId: cardId,
                ActorUserId: userId,
                FromColumnId: fromColumnId,
                FromColumnName: fromColumnName,
                ToColumnId: movedToColumnId,
                ToColumnName: movedToColumnName,
                NewOrder: card.Order));
        }

        if (shouldRecur && recurrenceTargetColumn != null)
        {
            await PublishOperationEventAsync(new RecurringCardResetEvent(
                CardId: cardId,
                ActorUserId: userId,
                FromColumnId: targetColumnId,
                FromColumnName: column.Name,
                ToColumnId: recurrenceTargetColumn.Id,
                ToColumnName: recurrenceTargetColumn.Name,
                NewOrder: card.Order));
        }

        return Ok(new
        {
            card.Id,
            card.ColumnId,
            DueDate = card.DueDate?.ToString("yyyy-MM-ddTHH:mm:ss"),
            ActualStartTime = card.ActualStartTime?.ToString("yyyy-MM-ddTHH:mm"),
            ActualEndTime = card.ActualEndTime?.ToString("yyyy-MM-ddTHH:mm"),
            RecurrenceApplied = shouldRecur,
            RecurrenceTargetColumnName = shouldRecur ? recurrenceTargetColumn?.Name : null
        });
    }

    [HttpPost]
    public async Task<IActionResult> MoveColumn(int columnId, int newOrder)
    {
        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        var oldOrder = column.Order;
        var columns = await db.KanbanColumns
            .Where(c => c.BoardId == column.BoardId && c.Id != columnId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var allColumns = new List<KanbanColumn>();
        var idx = 0;
        foreach (var existingColumn in columns)
        {
            if (idx == newOrder) allColumns.Add(column);
            allColumns.Add(existingColumn);
            idx++;
        }

        if (idx <= newOrder) allColumns.Add(column);

        for (var i = 0; i < allColumns.Count; i++)
            allColumns[i].Order = i;

        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new ColumnMovedEvent(
            ColumnId: columnId,
            ColumnName: column.Name,
            BoardId: column.BoardId,
            OldOrder: oldOrder,
            NewOrder: newOrder,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> MoveBoard(int boardId, int newOrder)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (board.UserId != userId) return Forbid();

        var oldOrder = board.Order;
        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId && b.Id != boardId)
            .OrderBy(b => b.Order)
            .ToListAsync();

        var allBoards = new List<KanbanBoard>();
        var idx = 0;
        foreach (var existing in boards)
        {
            if (idx == newOrder) allBoards.Add(board);
            allBoards.Add(existing);
            idx++;
        }

        if (idx <= newOrder) allBoards.Add(board);

        var orderValue = 0;
        foreach (var b in allBoards)
        {
            orderValue += 100;
            b.Order = orderValue;
        }

        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new BoardMovedEvent(
            BoardId: boardId,
            BoardName: board.Name,
            OldOrder: oldOrder,
            NewOrder: newOrder,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteColumn(int columnId)
    {
        var column = await db.KanbanColumns
            .Include(c => c.Cards)
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        db.KanbanCards.RemoveRange(column.Cards);
        db.KanbanColumns.Remove(column);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new ColumnDeletedEvent(
            ColumnId: columnId,
            ColumnName: column.Name,
            BoardId: column.BoardId,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameColumn(int columnId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Column name is required.");

        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        var oldName = column.Name;
        column.Name = name.Trim();
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new ColumnRenamedEvent(
            ColumnId: columnId,
            OldName: oldName,
            NewName: column.Name,
            BoardId: column.BoardId,
            ActorUserId: userId));
        return Ok(new { column.Id, column.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameBoard(int boardId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Board name is required.");

        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (board.UserId != userId) return Forbid();

        var oldName = board.Name;
        board.Name = name.Trim();
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new BoardRenamedEvent(
            BoardId: boardId,
            OldName: oldName,
            NewName: board.Name,
            ActorUserId: userId));
        return Ok(new { board.Id, board.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBoardOrder(int boardId, int order)
    {
        var board = await db.KanbanBoards.FindAsync(boardId);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (board.UserId != userId) return Forbid();

        board.Order = order;
        await db.SaveChangesAsync();
        return Ok(new { board.Id, board.Order });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBoard(int boardId)
    {
        var board = await db.KanbanBoards
            .Include(b => b.Columns)
                .ThenInclude(c => c.Cards)
                    .ThenInclude(c => c.CardLabels)
            .Include(b => b.BoardShares)
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (board.UserId != userId) return Forbid();

        foreach (var column in board.Columns.ToList())
        {
            db.KanbanCards.RemoveRange(column.Cards);
            db.KanbanColumns.Remove(column);
        }

        db.BoardShares.RemoveRange(board.BoardShares);
        db.KanbanBoards.Remove(board);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new BoardDeletedEvent(
            BoardId: boardId,
            BoardName: board.Name,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> UpdateColumnStatus(int columnId, int status)
    {
        var column = await db.KanbanColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId);
        if (column == null) return NotFound();

        if (!Enum.IsDefined(typeof(ColumnStatus), status))
            return BadRequest("Invalid column status.");

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(column.Board, userId)) return Forbid();

        var oldStatus = (int)column.ColumnStatus;
        column.ColumnStatus = (ColumnStatus)status;
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new ColumnStatusUpdatedEvent(
            ColumnId: columnId,
            ColumnName: column.Name,
            OldStatus: oldStatus,
            NewStatus: status,
            BoardId: column.BoardId,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> UpdateCardDetails(
        int cardId,
        string? title,
        string? description,
        DateTime? plannedStartTime,
        DateTime? dueDate,
        int priority = (int)Priority.None,
        string? assignedUserId = null,
        int? recurrenceInterval = null,
        int recurrenceUnit = (int)RecurrenceUnit.None)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Title is required.");

        if (!Enum.IsDefined(typeof(Priority), priority))
            return BadRequest("Invalid priority.");

        if (!Enum.IsDefined(typeof(RecurrenceUnit), recurrenceUnit))
            return BadRequest("Invalid recurrence unit.");

        if (recurrenceInterval is < 0)
            return BadRequest("Recurrence interval cannot be negative.");

        if (recurrenceInterval is > 365)
            return BadRequest("Recurrence interval cannot exceed 365.");

        if (recurrenceInterval is > 0 && recurrenceUnit == (int)RecurrenceUnit.None)
            return BadRequest("Recurrence unit is required when recurrence interval is set.");

        if (recurrenceInterval is > 0 && dueDate == null)
            return BadRequest("Due date is required when recurrence is set.");

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .Include(c => c.CreatorUser)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var normalizedAssignedUserId = NormalizeAssignedUserId(assignedUserId);
        if (!await CanAssignUserToBoardAsync(card.Column.Board, normalizedAssignedUserId))
            return BadRequest("Assigned user does not have access to this board.");

        var changedFields = new List<string>();
        if (!string.Equals(card.Title, title.Trim(), StringComparison.Ordinal))
            changedFields.Add("title");
        var newDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (!string.Equals(card.Description, newDescription, StringComparison.Ordinal))
            changedFields.Add("description");
        var newPlanned = plannedStartTime?.ToUniversalTime();
        if (card.PlannedStartTime != newPlanned)
            changedFields.Add("planned start time");
        var newDue = dueDate?.ToUniversalTime();
        if (card.DueDate != newDue)
            changedFields.Add("due date");
        if (card.Priority != (Priority)priority)
            changedFields.Add("priority");

        var newRecurrenceInterval = recurrenceInterval is > 0 ? recurrenceInterval : null;
        var newRecurrenceUnit = newRecurrenceInterval.HasValue ? (RecurrenceUnit)recurrenceUnit : RecurrenceUnit.None;
        if (card.RecurrenceInterval != newRecurrenceInterval || card.RecurrenceUnit != newRecurrenceUnit)
            changedFields.Add("recurrence");

        var oldAssigneeId = card.AssignedUserId;

        card.Title = title.Trim();
        card.Description = newDescription;
        card.PlannedStartTime = newPlanned;
        card.DueDate = newDue;
        card.Priority = (Priority)priority;
        card.AssignedUserId = normalizedAssignedUserId;
        card.RecurrenceInterval = newRecurrenceInterval;
        card.RecurrenceUnit = newRecurrenceUnit;

        await db.SaveChangesAsync();

        if (changedFields.Count > 0)
        {
            await PublishOperationEventAsync(new CardUpdatedEvent(
                CardId: cardId,
                ActorUserId: userId,
                ChangedFields: changedFields));
        }

        if (oldAssigneeId != normalizedAssignedUserId)
        {
            await PublishOperationEventAsync(new CardAssignedEvent(
                CardId: cardId,
                ActorUserId: userId,
                OldAssigneeId: oldAssigneeId,
                NewAssigneeId: normalizedAssignedUserId));
        }

        var assignedUser = normalizedAssignedUserId == null
            ? null
            : await userManager.FindByIdAsync(normalizedAssignedUserId);

        return Ok(new
        {
            card.Id,
            card.Title,
            card.Description,
            PlannedStartTime = card.PlannedStartTime?.ToString("yyyy-MM-dd"),
            DueDate = card.DueDate?.ToString("yyyy-MM-dd"),
            ActualStartTime = card.ActualStartTime?.ToString("yyyy-MM-ddTHH:mm"),
            ActualEndTime = card.ActualEndTime?.ToString("yyyy-MM-ddTHH:mm"),
            CreationTime = card.CreationTime.ToString("yyyy-MM-ddTHH:mm"),
            Priority = (int)card.Priority,
            PriorityText = card.Priority.ToString(),
            card.RecurrenceInterval,
            RecurrenceUnit = (int)card.RecurrenceUnit,
            AssignedUserId = assignedUser?.Id,
            AssignedUserName = GetUserDisplayName(assignedUser),
            AssignedUserInitial = GetUserInitial(assignedUser),
            AssignedUserAvatarUrl = GetUserAvatarUrl(assignedUser),
            CreatorUserId = card.CreatorUser?.Id,
            CreatorUserName = GetUserDisplayName(card.CreatorUser),
            CreatorUserInitial = GetUserInitial(card.CreatorUser),
            CreatorUserAvatarUrl = GetUserAvatarUrl(card.CreatorUser)
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateCardPriority(int cardId, int priority)
    {
        if (!Enum.IsDefined(typeof(Priority), priority))
            return BadRequest("Invalid priority.");

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var oldPriority = card.Priority;
        card.Priority = (Priority)priority;
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new CardPriorityUpdatedEvent(
            CardId: card.Id,
            ActorUserId: userId,
            OldPriority: oldPriority,
            NewPriority: card.Priority));

        return Ok(new
        {
            card.Id,
            Priority = (int)card.Priority,
            PriorityText = card.Priority.ToString()
        });
    }

    [HttpPost]
    public async Task<IActionResult> AssignCard(int cardId, string? assignedUserId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var normalizedAssignedUserId = NormalizeAssignedUserId(assignedUserId);
        if (!await CanAssignUserToBoardAsync(card.Column.Board, normalizedAssignedUserId))
            return BadRequest("Assigned user does not have access to this board.");

        var oldAssigneeId = card.AssignedUserId;
        card.AssignedUserId = normalizedAssignedUserId;
        await db.SaveChangesAsync();

        if (oldAssigneeId != normalizedAssignedUserId)
        {
            await PublishOperationEventAsync(new CardAssignedEvent(
                CardId: cardId,
                ActorUserId: userId,
                OldAssigneeId: oldAssigneeId,
                NewAssigneeId: normalizedAssignedUserId));
        }

        var assignedUser = normalizedAssignedUserId == null
            ? null
            : await userManager.FindByIdAsync(normalizedAssignedUserId);

        return Ok(new
        {
            card.Id,
            AssignedUserId = assignedUser?.Id,
            AssignedUserName = GetUserDisplayName(assignedUser),
            AssignedUserInitial = GetUserInitial(assignedUser),
            AssignedUserAvatarUrl = GetUserAvatarUrl(assignedUser)
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetBoardMembers(int boardId)
    {
        var board = await db.KanbanBoards.FirstOrDefaultAsync(b => b.Id == boardId);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(board, userId)) return Forbid();

        var accessibleUserIds = await GetAccessibleBoardUserIdsAsync(board);
        var users = await db.Users
            .Where(u => accessibleUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.UserName)
            .ToListAsync();

        var members = users.Select(u => new
        {
            u.Id,
            DisplayName = GetUserDisplayName(u),
            UserName = u.UserName ?? u.Email ?? u.Id,
            Initial = GetUserInitial(u)
        });

        return Ok(members);
    }

    [HttpPost]
    public async Task<IActionResult> AddLabel(int cardId, string name, string? color)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Label name is required.");

        var normalizedName = name.Trim();
        if (normalizedName.Length > 100)
            return BadRequest("Label name is too long.");

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var normalizedUpperName = normalizedName.ToUpperInvariant();
        var label = await db.KanbanLabels
            .FirstOrDefaultAsync(l => l.Name.ToUpper() == normalizedUpperName);

        if (label == null)
        {
            var chosenColor = LabelColors[Random.Shared.Next(LabelColors.Length)];
            if (!string.IsNullOrWhiteSpace(color))
            {
                var normalizedColor = color.Trim();
                if (HexColorRegex.IsMatch(normalizedColor))
                {
                    chosenColor = normalizedColor;
                }
            }

            label = new KanbanLabel
            {
                Name = normalizedName,
                Color = chosenColor
            };
            db.KanbanLabels.Add(label);
        }

        var alreadyLinked = await db.KanbanCardLabels
            .AnyAsync(link => link.CardId == cardId && link.LabelId == label.Id);
        if (!alreadyLinked)
        {
            db.KanbanCardLabels.Add(new KanbanCardLabel
            {
                CardId = cardId,
                Label = label
            });
        }

        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new LabelAddedEvent(
            CardId: cardId,
            LabelId: label.Id,
            LabelName: label.Name,
            LabelColor: label.Color,
            BoardId: card.Column.BoardId,
            ActorUserId: userId));

        return Ok(new { label.Id, label.Name, label.Color });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveLabel(int cardId, int labelId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var cardLabel = await db.KanbanCardLabels
            .Include(link => link.Label)
            .FirstOrDefaultAsync(link => link.CardId == cardId && link.LabelId == labelId);
        if (cardLabel == null) return NotFound();

        var labelName = cardLabel.Label.Name;
        db.KanbanCardLabels.Remove(cardLabel);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new LabelRemovedEvent(
            CardId: cardId,
            LabelId: labelId,
            LabelName: labelName,
            BoardId: card.Column.BoardId,
            ActorUserId: userId));
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> UpdateLabelColor(int cardId, int labelId, string color)
    {
        var normalizedColor = color.Trim();
        if (!HexColorRegex.IsMatch(normalizedColor))
            return BadRequest("Color must be a hex value like #FF5733.");

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var label = await db.KanbanCardLabels
            .Where(link => link.CardId == cardId && link.LabelId == labelId)
            .Select(link => link.Label)
            .FirstOrDefaultAsync();
        if (label == null) return NotFound();

        var oldColor = label.Color;
        label.Color = normalizedColor;
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new LabelColorUpdatedEvent(
            CardId: cardId,
            LabelId: labelId,
            LabelName: label.Name,
            OldColor: oldColor,
            NewColor: normalizedColor,
            BoardId: card.Column.BoardId,
            ActorUserId: userId));

        return Ok(new { label.Id, label.Name, label.Color });
    }

    [HttpGet]
    public async Task<IActionResult> SearchLabels(string? q)
    {
        var labels = db.KanbanLabels.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var normalizedQuery = q.Trim().ToUpperInvariant();
            labels = labels.Where(label => label.Name.ToUpper().Contains(normalizedQuery));
        }

        var results = await labels
            .OrderByDescending(label => label.CardLabels.Count)
            .ThenBy(label => label.Name)
            .Take(10)
            .Select(label => new { label.Id, label.Name, label.Color })
            .ToListAsync();

        return Ok(results);
    }

    [HttpGet]
    public async Task<IActionResult> ManageBoard(int id)
    {
        var board = await db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (board.UserId != userId) return Forbid();

        return this.StackView(new ManageBoardViewModel
        {
            BoardId = board.Id,
            BoardName = board.Name,
            BoardOrder = board.Order,
            Columns = board.Columns.OrderBy(c => c.Order).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> ManageShares(int id)
    {
        var board = await db.KanbanBoards
            .Include(b => b.BoardShares)
                .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isOwner = board.UserId == userId;
        var canManage = isOwner ||
            (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyBoardShare)).Succeeded;

        if (!canManage) return NotFound();

        var allRoles = await db.Roles.ToListAsync();
        var allUsers = await db.Users.Where(u => u.Id != userId).ToListAsync();
        var publicLink = Url.Action("View", "PublicKanban", new { boardId = board.Id }, Request.Scheme);

        return this.StackView(new ManageSharesViewModel(board.Name)
        {
            BoardId = board.Id,
            BoardName = board.Name,
            IsPublic = board.IsPublic,
            PublicLink = publicLink,
            ExistingShares = board.BoardShares.OrderByDescending(s => s.CreationTime).ToList(),
            AvailableRoles = allRoles,
            AvailableUsers = allUsers
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVisibility(int id, bool publicAccess)
    {
        var board = await db.KanbanBoards.FindAsync(id);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isOwner = board.UserId == userId;
        var canManage = isOwner ||
            (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyBoardShare)).Succeeded;

        if (!canManage) return NotFound();

        board.IsPublic = publicAccess;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageShares), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShare(int id, AddShareViewModel model)
    {
        var board = await db.KanbanBoards.FindAsync(id);
        if (board == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isOwner = board.UserId == userId;
        var canManage = isOwner ||
            (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyBoardShare)).Succeeded;

        if (!canManage) return NotFound();

        if (!ModelState.IsValid)
            return RedirectToAction(nameof(ManageShares), new { id, error = "invalid" });

        var targetUserId = string.IsNullOrWhiteSpace(model.TargetUserId) ? null : model.TargetUserId;
        var targetRoleId = string.IsNullOrWhiteSpace(model.TargetRoleId) ? null : model.TargetRoleId;

        if (targetUserId == null && targetRoleId == null)
            return RedirectToAction(nameof(ManageShares), new { id, error = "invalid" });

        var exists = await db.BoardShares.AnyAsync(s =>
            s.BoardId == id &&
            ((targetUserId != null && s.SharedWithUserId == targetUserId) ||
             (targetRoleId != null && s.SharedWithRoleId == targetRoleId)));

        if (exists)
            return RedirectToAction(nameof(ManageShares), new { id, error = "duplicate" });

        db.BoardShares.Add(new BoardShare
        {
            Id = Guid.NewGuid(),
            BoardId = id,
            SharedWithUserId = targetUserId,
            SharedWithRoleId = targetRoleId,
            Permission = model.Permission
        });
        await db.SaveChangesAsync();

        if (targetUserId != null)
        {
            await PublishOperationEventAsync(new BoardSharedEvent(
                BoardId: id,
                ActorUserId: userId,
                SharedWithUserId: targetUserId));
        }

        return RedirectToAction(nameof(ManageShares), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveShare(Guid id)
    {
        var share = await db.BoardShares
            .Include(s => s.Board)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (share == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        var isOwner = share.Board.UserId == userId;
        var canManage = isOwner ||
            (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyBoardShare)).Succeeded;

        if (!canManage) return Forbid();

        db.BoardShares.Remove(share);
        await db.SaveChangesAsync();

        return RedirectToAction(nameof(ManageShares), new { id = share.BoardId });
    }

    [HttpPost]
    public async Task<IActionResult> AddComment(int cardId, string content, string? images)
    {
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest("Content is required.");

        if (content.Trim().Length > 2000)
            return BadRequest("Content is too long.");

        images = images ?? string.Empty;

        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var comment = new KanbanCardComment
        {
            CardId = cardId,
            Content = content.Trim(),
            AuthorId = userId,
            Images = images
        };
        db.KanbanCardComments.Add(comment);
        await db.SaveChangesAsync();

        await PublishOperationEventAsync(new CardCommentAddedEvent(
            CardId: cardId,
            CommentId: comment.Id,
            ActorUserId: userId));

        var author = await userManager.FindByIdAsync(userId);
        return Ok(new
        {
            comment.Id,
            comment.Content,
            comment.CreationTime,
            AuthorName = GetUserDisplayName(author),
            AuthorInitial = GetUserInitial(author),
            comment.Images
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetComments(int cardId)
    {
        var card = await db.KanbanCards
            .Include(c => c.Column)
                .ThenInclude(col => col.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);
        if (card == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (!await HasReadAccess(card.Column.Board, userId)) return Forbid();

        var commentsList = await db.KanbanCardComments
            .Where(c => c.CardId == cardId)
            .Include(c => c.Author)
            .OrderBy(c => c.CreationTime)
            .ToListAsync();

        var comments = commentsList.Select(c => new
        {
            c.Id,
            c.Content,
            c.CreationTime,
            c.Images,
            AuthorName = GetUserDisplayName(c.Author),
            AuthorInitial = GetUserInitial(c.Author),
            Avatar = GetUserAvatarUrl(c.Author)
        });

        return Ok(comments);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var userId = userManager.GetUserId(User)!;
        var comment = await db.KanbanCardComments.Include(c => c.Card).ThenInclude(c => c.Column).ThenInclude(col => col.Board).FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment == null) return NotFound();

        if (!await HasEditAccess(comment.Card.Column.Board, userId)) return Forbid();

        // Only the author or board admin can delete
        if (comment.AuthorId != userId)
        {
            var boardAdminId = comment.Card.Column.Board.UserId;
            if (userId != boardAdminId) return Forbid();
        }

        db.Remove(comment);
        await db.SaveChangesAsync();
        await PublishOperationEventAsync(new CardCommentDeletedEvent(
            CardId: comment.CardId,
            CommentId: comment.Id,
            ActorUserId: userId,
            CardTitle: comment.Card.Title,
            BoardName: comment.Card.Column.Board.Name));
        return Ok();
    }

    private Task<KanbanBoard?> LoadBoardAsync(int boardId)
    {
        return db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(card => card.Order))
                    .ThenInclude(card => card.CardLabels)
                        .ThenInclude(link => link.Label)
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(card => card.Order))
                    .ThenInclude(card => card.AssignedUser)
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(card => card.Order))
                    .ThenInclude(card => card.CreatorUser)
            .FirstOrDefaultAsync(b => b.Id == boardId);
    }

    private async Task<HashSet<string>> GetAccessibleBoardUserIdsAsync(KanbanBoard board)
    {
        var accessibleUserIds = await db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithUserId != null)
            .Select(share => share.SharedWithUserId!)
            .ToHashSetAsync();

        var roleIds = await db.BoardShares
            .Where(share => share.BoardId == board.Id && share.SharedWithRoleId != null)
            .Select(share => share.SharedWithRoleId!)
            .ToListAsync();

        var roleUserIds = await db.UserRoles
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId)
            .ToListAsync();
        accessibleUserIds.UnionWith(roleUserIds);

        if (!string.IsNullOrWhiteSpace(board.UserId))
            accessibleUserIds.Add(board.UserId);

        return accessibleUserIds;
    }

    private async Task<bool> CanAssignUserToBoardAsync(KanbanBoard board, string? assignedUserId)
    {
        if (assignedUserId == null) return true;
        if (!await db.Users.AnyAsync(user => user.Id == assignedUserId)) return false;
        return (await GetAccessibleBoardUserIdsAsync(board)).Contains(assignedUserId);
    }

    private async Task<bool> HasReadAccess(KanbanBoard board, string userId)
    {
        if (board.IsPublic) return true;
        if (board.UserId == userId) return true;
        var userRoles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        return await db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    private async Task<bool> HasEditAccess(KanbanBoard board, string userId)
    {
        if (board.UserId == userId) return true;
        var userRoles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        return await db.BoardShares.AnyAsync(s =>
            s.BoardId == board.Id &&
            s.Permission == SharePermission.Editable &&
            (s.SharedWithUserId == userId ||
             (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    private static string? NormalizeAssignedUserId(string? assignedUserId)
    {
        return string.IsNullOrWhiteSpace(assignedUserId) ? null : assignedUserId.Trim();
    }

    private async Task PublishOperationEventAsync<TNotification>(TNotification notification)
        where TNotification : INotification
    {
        try
        {
            await mediator.Publish(notification);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish operation event {OperationEvent}", typeof(TNotification).Name);
        }
    }

    private static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }

    private string? GetUserAvatarUrl(User? user)
    {
        if (user == null || user.AvatarRelativePath == Aiursoft.Kanban.Entities.User.DefaultAvatarPath)
            return null;

        return $"{storage.RelativePathToInternetUrl(user.AvatarRelativePath)}?w=56&square=true";
    }

    private static string GetUserInitial(User? user)
    {
        var displayName = GetUserDisplayName(user);
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim()[0].ToString().ToUpperInvariant();
    }

    private static DateTime AdvanceByRecurrence(DateTime baseline, int interval, RecurrenceUnit unit)
    {
        return unit switch
        {
            RecurrenceUnit.Day => baseline.AddDays(interval),
            RecurrenceUnit.Week => baseline.AddDays(7 * interval),
            RecurrenceUnit.Month => baseline.AddMonths(interval),
            RecurrenceUnit.Year => baseline.AddYears(interval),
            _ => baseline
        };
    }
}
