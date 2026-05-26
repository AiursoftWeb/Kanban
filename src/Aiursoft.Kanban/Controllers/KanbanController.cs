using System.Text.RegularExpressions;
using Aiursoft.Kanban.Authorization;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Models.KanbanViewModels;
using Aiursoft.Kanban.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
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
    IAuthorizationService authorizationService) : Controller
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
        CascadedLinksGroupName = "Kanban",
        CascadedLinksIcon = "columns-3",
        CascadedLinksOrder = 2,
        LinkText = "My Boards",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(int? boardId)
    {
        var userId = userManager.GetUserId(User)!;

        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Include(b => b.Columns)
            .OrderByDescending(b => b.CreationTime)
            .ToListAsync();

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
            CurrentBoard = currentBoard,
            IsOwner = currentBoard == null || currentBoard.UserId == userId,
            CanEditCurrentBoard = currentBoard == null || canEditCurrentBoard
        });
    }

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 2,
        CascadedLinksGroupName = "Kanban",
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

        return this.StackView(new SharedWithMeViewModel
        {
            Shares = shares,
            RoleNames = roleNames
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBoard(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest();

        var userId = userManager.GetUserId(User)!;
        var board = new KanbanBoard { Name = name.Trim(), UserId = userId };
        db.KanbanBoards.Add(board);

        var defaultColumns = new[]
        {
            new KanbanColumn { Name = "To Do", Order = 0, Board = board, ColumnStatus = ColumnStatus.NotStarted },
            new KanbanColumn { Name = "In Progress", Order = 1, Board = board, ColumnStatus = ColumnStatus.InProgress },
            new KanbanColumn { Name = "Done", Order = 2, Board = board, ColumnStatus = ColumnStatus.Completed }
        };
        db.KanbanColumns.AddRange(defaultColumns);

        await db.SaveChangesAsync();
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
            ColumnId = columnId
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync();

        return Ok(new { card.Id, card.Title, card.Description, card.Order, card.ColumnId });
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

        var userId = userManager.GetUserId(User)!;
        if (!await HasEditAccess(card.Column.Board, userId)) return Forbid();

        var now = DateTime.UtcNow;
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

        await db.SaveChangesAsync();
        return Ok(new
        {
            card.Id,
            ActualStartTime = card.ActualStartTime?.ToString("yyyy-MM-ddTHH:mm"),
            ActualEndTime = card.ActualEndTime?.ToString("yyyy-MM-ddTHH:mm")
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

        column.Name = name.Trim();
        await db.SaveChangesAsync();
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

        board.Name = name.Trim();
        await db.SaveChangesAsync();
        return Ok(new { board.Id, board.Name });
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

        column.ColumnStatus = (ColumnStatus)status;
        await db.SaveChangesAsync();
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
        string? assignedUserId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Title is required.");

        if (!Enum.IsDefined(typeof(Priority), priority))
            return BadRequest("Invalid priority.");

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

        card.Title = title.Trim();
        card.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        card.PlannedStartTime = plannedStartTime?.ToUniversalTime();
        card.DueDate = dueDate?.ToUniversalTime();
        card.Priority = (Priority)priority;
        card.AssignedUserId = normalizedAssignedUserId;

        await db.SaveChangesAsync();

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
            Priority = (int)card.Priority,
            PriorityText = card.Priority.ToString(),
            AssignedUserId = assignedUser?.Id,
            AssignedUserName = GetUserDisplayName(assignedUser),
            AssignedUserInitial = GetUserInitial(assignedUser)
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

        card.Priority = (Priority)priority;
        await db.SaveChangesAsync();

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

        card.AssignedUserId = normalizedAssignedUserId;
        await db.SaveChangesAsync();

        var assignedUser = normalizedAssignedUserId == null
            ? null
            : await userManager.FindByIdAsync(normalizedAssignedUserId);

        return Ok(new
        {
            card.Id,
            AssignedUserId = assignedUser?.Id,
            AssignedUserName = GetUserDisplayName(assignedUser),
            AssignedUserInitial = GetUserInitial(assignedUser)
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
    public async Task<IActionResult> AddLabel(int cardId, string name)
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
            label = new KanbanLabel
            {
                Name = normalizedName,
                Color = LabelColors[Random.Shared.Next(LabelColors.Length)]
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
            .FirstOrDefaultAsync(link => link.CardId == cardId && link.LabelId == labelId);
        if (cardLabel == null) return NotFound();

        db.KanbanCardLabels.Remove(cardLabel);
        await db.SaveChangesAsync();
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

        label.Color = normalizedColor;
        await db.SaveChangesAsync();

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

    private static string? GetUserDisplayName(User? user)
    {
        return user == null ? null : string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.UserName ?? user.Email ?? user.Id
            : user.DisplayName;
    }

    private static string GetUserInitial(User? user)
    {
        var displayName = GetUserDisplayName(user);
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim()[0].ToString().ToUpperInvariant();
    }
}
