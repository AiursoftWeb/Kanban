using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Notifications;

/// <summary>
/// Extracts @mentioned user IDs from text content by matching @DisplayName
/// patterns against board members.
/// </summary>
public static class MentionParser
{
    /// <summary>
    /// Finds all board members whose DisplayName appears after an @ in the given text.
    /// Returns distinct user IDs of matched members.
    /// </summary>
    public static async Task<HashSet<string>> ExtractMentionedUserIds(
        TemplateDbContext db, string text, int boardId, CancellationToken ct)
    {
        var result = new HashSet<string>();

        // Collect all board members (accessible users)
        var memberNames = await GetBoardMemberNames(db, boardId, ct);
        if (memberNames.Count == 0) return result;

        // Find all @ positions in the text
        var atPositions = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '@')
            {
                // @ must be at start of text or preceded by whitespace/punctuation
                if (i == 0 || char.IsWhiteSpace(text[i - 1]) || char.IsPunctuation(text[i - 1]))
                {
                    atPositions.Add(i);
                }
            }
        }

        foreach (var atPos in atPositions)
        {
            // Try matching board member names at this position (longest match first)
            var candidateStart = atPos + 1;
            foreach (var (displayName, userId) in memberNames.OrderByDescending(kv => kv.DisplayName.Length))
            {
                if (candidateStart + displayName.Length > text.Length) continue;

                var match = string.Compare(
                    text, candidateStart, displayName, 0, displayName.Length,
                    StringComparison.OrdinalIgnoreCase) == 0;

                if (!match) continue;

                // Check that the match ends at a word boundary
                var endPos = candidateStart + displayName.Length;
                if (endPos < text.Length && !char.IsWhiteSpace(text[endPos]) && !char.IsPunctuation(text[endPos]))
                    continue;

                result.Add(userId);
                break; // Longest match already found for this @
            }
        }

        return result;
    }

    private static async Task<List<(string DisplayName, string UserId)>> GetBoardMemberNames(
        TemplateDbContext db, int boardId, CancellationToken ct)
    {
        var userIds = new HashSet<string>();

        // Board owner
        var board = await db.KanbanBoards
            .Where(b => b.Id == boardId)
            .Select(b => new { b.UserId, b.IsPublic })
            .FirstOrDefaultAsync(ct);

        if (board == null) return [];

        if (!string.IsNullOrWhiteSpace(board.UserId))
            userIds.Add(board.UserId);

        // Direct share recipients
        var directIds = await db.BoardShares
            .Where(s => s.BoardId == boardId && s.SharedWithUserId != null)
            .Select(s => s.SharedWithUserId!)
            .ToListAsync(ct);
        foreach (var id in directIds) userIds.Add(id);

        // Role-based share recipients
        var roleIds = await db.BoardShares
            .Where(s => s.BoardId == boardId && s.SharedWithRoleId != null)
            .Select(s => s.SharedWithRoleId!)
            .ToListAsync(ct);

        if (roleIds.Count > 0)
        {
            var roleUserIds = await db.UserRoles
                .Where(ur => roleIds.Contains(ur.RoleId))
                .Select(ur => ur.UserId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in roleUserIds) userIds.Add(id);
        }

        if (userIds.Count == 0) return [];

        // Get DisplayName + Id for all board members
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);

        return users
            .Select(u => (u.DisplayName, u.Id))
            .ToList();
    }
}
