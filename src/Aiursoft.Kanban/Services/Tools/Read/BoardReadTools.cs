using System.ComponentModel;
using Aiursoft.Kanban.Entities;
using Aiursoft.Kanban.Services.Access;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Tools.Read;

[McpServerToolType]
public class BoardReadTools(
    TemplateDbContext db,
    UserManager<User> userManager,
    KanbanAccessService access,
    CurrentUserService currentUser) : IScopedDependency
{
    [McpServerTool, Description("Get all kanban boards owned by the current user")]
    public async Task<string> GetUserBoards()
    {
        var userId = currentUser.UserId;
        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Include(b => b.Columns)
            .OrderBy(b => b.Order)
            .ToListAsync();

        if (boards.Count == 0)
            return "You don't have any boards yet.";

        var lines = new List<string> { $"Found {boards.Count} board(s):" };
        foreach (var board in boards)
        {
            var columnNames = board.Columns.OrderBy(c => c.Order).Select(c => c.Name);
            lines.Add($"- Board #{board.Id} \"{board.Name}\" (Columns: {string.Join(", ", columnNames)})");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get detailed information about a specific board by its ID")]
    public async Task<string> GetBoardById(
        [Description("Board ID")] int boardId)
    {
        var userId = currentUser.UserId;
        var board = await db.KanbanBoards
            .Include(b => b.Columns.OrderBy(c => c.Order))
                .ThenInclude(c => c.Cards.OrderBy(card => card.Order))
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board == null) return "Board not found.";
        if (!await access.HasReadAccess(board, userId)) return "You do not have access to this board.";

        var lines = new List<string> { $"Board #{board.Id}: \"{board.Name}\"" };
        foreach (var col in board.Columns)
        {
            lines.Add($"  Column \"{col.Name}\" (#{col.Id}, Status: {col.ColumnStatus}):");
            foreach (var card in col.Cards)
            {
                var assignee = card.AssignedUserId != null
                    ? (await userManager.FindByIdAsync(card.AssignedUserId))?.DisplayName ?? card.AssignedUserId
                    : "unassigned";
                lines.Add($"    - #{card.Id} [{card.Priority}] {card.Title} (assigned: {assignee})");
            }
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description(
        "List all boards accessible to the current user, including owned boards, public boards, " +
        "and boards shared with the user or their roles. Supports pagination — max 20 boards per page.")]
    public async Task<string> GetBoards(
        [Description("Page number (1-based, default 1)")] int page = 1,
        [Description("Boards per page (max 20, default 20)")] int pageSize = 20)
    {
        var userId = currentUser.UserId;

        // ── Collect all accessible board IDs ──
        // Owned boards
        var ownedIds = await db.KanbanBoards
            .Where(b => b.UserId == userId)
            .Select(b => b.Id)
            .ToListAsync();

        // Public boards
        var publicIds = await db.KanbanBoards
            .Where(b => b.IsPublic && b.UserId != userId)
            .Select(b => b.Id)
            .ToListAsync();

        // Shared boards (via user or role)
        var userRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var sharedIds = await db.BoardShares
            .Where(s => s.SharedWithUserId == userId ||
                        (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .Select(s => s.BoardId)
            .Distinct()
            .ToListAsync();

        var allIds = ownedIds
            .Concat(publicIds)
            .Concat(sharedIds)
            .Distinct()
            .ToList();

        if (allIds.Count == 0)
            return "You don't have access to any boards.";

        // ── Paginate ──
        pageSize = Math.Clamp(pageSize, 1, 20);
        var totalCount = allIds.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var pagedIds = allIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var boards = await db.KanbanBoards
            .Where(b => pagedIds.Contains(b.Id))
            .Include(b => b.User)
            .Include(b => b.Columns)
            .ToListAsync();

        // Restore pagination order
        var orderedBoards = pagedIds
            .Select(id => boards.FirstOrDefault(b => b.Id == id))
            .Where(b => b != null)
            .ToList();

        var lines = new List<string>
        {
            $"Found {totalCount} accessible board(s). Page {page} of {totalPages} (showing {orderedBoards.Count}):"
        };

        foreach (var board in orderedBoards)
        {
            var ownerName = board!.UserId == userId
                ? "you"
                : KanbanAccessService.GetUserDisplayName(board.User);
            var accessLabel = board.UserId == userId
                ? "owned"
                : board.IsPublic
                    ? "public"
                    : "shared";
            lines.Add($"- Board #{board.Id} \"{board.Name}\" (Owner: {ownerName}, Access: {accessLabel}, Columns: {board.Columns.Count})");
        }

        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Search boards by name")]
    public async Task<string> SearchBoards(
        [Description("Search query")] string query)
    {
        var userId = currentUser.UserId;
        var normalized = query.Trim().ToUpperInvariant();
        var boards = await db.KanbanBoards
            .Where(b => b.UserId == userId && b.Name.ToUpper().Contains(normalized))
            .OrderBy(b => b.Order)
            .ToListAsync();

        if (boards.Count == 0) return $"No boards found matching \"{query}\".";
        return string.Join("\n", boards.Select(b => $"- Board #{b.Id} \"{b.Name}\""));
    }

    [McpServerTool, Description("Get all publicly visible kanban boards")]
    public async Task<string> GetPublicBoards()
    {
        var boards = await db.KanbanBoards
            .Where(b => b.IsPublic)
            .Include(b => b.Columns)
            .Include(b => b.User)
            .OrderBy(b => b.Order)
            .ToListAsync();

        if (boards.Count == 0) return "There are no public boards.";

        var lines = new List<string> { $"Found {boards.Count} public board(s):" };
        foreach (var board in boards)
        {
            var ownerName = KanbanAccessService.GetUserDisplayName(board.User);
            lines.Add($"- Board #{board.Id} \"{board.Name}\" (Owner: {ownerName}, Columns: {board.Columns.Count})");
        }
        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Get boards shared with the current user (not owned by them)")]
    public async Task<string> GetSharedBoards()
    {
        var userId = currentUser.UserId;
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return "User not found.";

        var userRoles = await userManager.GetRolesAsync(user);
        var userRoleIds = await db.Roles
            .Where(r => userRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        var shares = await db.BoardShares
            .Include(s => s.Board).ThenInclude(b => b.Columns)
            .Include(s => s.Board).ThenInclude(b => b.User)
            .Where(s => s.SharedWithUserId == userId ||
                        (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync();

        if (shares.Count == 0) return "No boards have been shared with you.";

        var lines = new List<string> { $"Found {shares.Count} board(s) shared with you:" };
        foreach (var share in shares)
        {
            var board = share.Board;
            var ownerName = KanbanAccessService.GetUserDisplayName(board.User);
            var permStr = share.Permission == SharePermission.Editable ? "Edit" : "Read-only";
            lines.Add($"- Board #{board.Id} \"{board.Name}\" (Owner: {ownerName}, Permission: {permStr}, Columns: {board.Columns.Count})");
        }
        return string.Join("\n", lines);
    }
}
