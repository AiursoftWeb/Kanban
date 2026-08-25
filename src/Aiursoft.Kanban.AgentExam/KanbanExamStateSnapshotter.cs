using System.Text.Json;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.Kanban.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Kanban.Services.Agent.Exam;

public sealed class KanbanExamStateSnapshotter(TemplateDbContext db)
{
    public async Task<JsonElement> CaptureAsync(
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken = default)
    {
        await AddGeneratedAliases(aliases, cancellationToken);
        var usersById = Reverse(aliases.Users);
        var boardsById = Reverse(aliases.Boards);
        var columnsById = Reverse(aliases.Columns);
        var cardsById = Reverse(aliases.Cards);
        var labelsById = Reverse(aliases.Labels);
        var commentsById = Reverse(aliases.Comments);
        var rolesById = Reverse(aliases.Roles);

        var userRoles = await db.UserRoles.AsNoTracking().ToArrayAsync(cancellationToken);
        var snapshot = new KanbanExamStateSnapshot(
            Users: (await db.Users.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(user => new KanbanExamUserState(
                    RequiredAlias(usersById, user.Id, "user"),
                    user.DisplayName,
                    userRoles.Where(role => role.UserId == user.Id)
                        .Select(role => RequiredAlias(rolesById, role.RoleId, "role"))
                        .Order(StringComparer.Ordinal)
                        .ToArray()))
                .OrderBy(user => user.Id, StringComparer.Ordinal)
                .ToArray(),
            Boards: (await db.KanbanBoards.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(board => new KanbanExamBoardState(
                    RequiredAlias(boardsById, board.Id, "board"),
                    board.Name,
                    Alias(usersById, board.UserId),
                    board.IsPublic,
                    board.IsArchived,
                    board.Order))
                .OrderBy(board => board.Id, StringComparer.Ordinal)
                .ToArray(),
            Columns: (await db.KanbanColumns.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(column => new KanbanExamColumnState(
                    RequiredAlias(columnsById, column.Id, "column"),
                    RequiredAlias(boardsById, column.BoardId, "board"),
                    column.Name,
                    column.ColumnStatus.ToString(),
                    column.Order))
                .OrderBy(column => column.Id, StringComparer.Ordinal)
                .ToArray(),
            Cards: (await db.KanbanCards.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(card => new KanbanExamCardState(
                    RequiredAlias(cardsById, card.Id, "card"),
                    RequiredAlias(columnsById, card.ColumnId, "column"),
                    card.Title,
                    card.Description ?? string.Empty,
                    Alias(usersById, card.CreatorUserId),
                    Alias(usersById, card.AssignedUserId),
                    card.Priority.ToString(),
                    card.DueDate,
                    card.Order))
                .OrderBy(card => card.Id, StringComparer.Ordinal)
                .ToArray(),
            Shares: (await db.BoardShares.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(share => new KanbanExamShareState(
                    RequiredAlias(boardsById, share.BoardId, "board"),
                    Alias(usersById, share.SharedWithUserId),
                    Alias(rolesById, share.SharedWithRoleId),
                    share.Permission.ToString()))
                .OrderBy(share => share.BoardId, StringComparer.Ordinal)
                .ThenBy(share => share.UserId, StringComparer.Ordinal)
                .ThenBy(share => share.RoleName, StringComparer.Ordinal)
                .ToArray(),
            Labels: (await db.KanbanLabels.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(label => new KanbanExamLabelState(
                    RequiredAlias(labelsById, label.Id, "label"),
                    label.Name,
                    label.Color))
                .OrderBy(label => label.Id, StringComparer.Ordinal)
                .ToArray(),
            CardLabels: (await db.KanbanCardLabels.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(link => new KanbanExamCardLabelState(
                    RequiredAlias(cardsById, link.CardId, "card"),
                    RequiredAlias(labelsById, link.LabelId, "label")))
                .OrderBy(link => link.CardId, StringComparer.Ordinal)
                .ThenBy(link => link.LabelId, StringComparer.Ordinal)
                .ToArray(),
            Comments: (await db.KanbanCardComments.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(comment => new KanbanExamCommentState(
                    RequiredAlias(commentsById, comment.Id, "comment"),
                    RequiredAlias(cardsById, comment.CardId, "card"),
                    RequiredAlias(usersById, comment.AuthorId, "user"),
                    comment.Content))
                .OrderBy(comment => comment.Id, StringComparer.Ordinal)
                .ToArray(),
            Subscriptions: (await db.KanbanCardSubscriptions.AsNoTracking().ToArrayAsync(cancellationToken))
                .Select(subscription => new KanbanExamSubscriptionState(
                    RequiredAlias(cardsById, subscription.CardId, "card"),
                    RequiredAlias(usersById, subscription.UserId, "user")))
                .OrderBy(subscription => subscription.CardId, StringComparer.Ordinal)
                .ThenBy(subscription => subscription.UserId, StringComparer.Ordinal)
                .ToArray());

        return JsonSerializer.SerializeToElement(snapshot, JsonDefaults.Options);
    }

    private async Task AddGeneratedAliases(
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var cardId in await db.KanbanCards.AsNoTracking()
                     .Select(card => card.Id)
                     .ToArrayAsync(cancellationToken))
        {
            if (!aliases.Cards.Values.Contains(cardId))
            {
                aliases.AddCard($"generated.card-{cardId}", cardId);
            }
        }
        foreach (var labelId in await db.KanbanLabels.AsNoTracking()
                     .Select(label => label.Id)
                     .ToArrayAsync(cancellationToken))
        {
            if (!aliases.Labels.Values.Contains(labelId))
            {
                aliases.AddLabel($"generated.label-{labelId}", labelId);
            }
        }
        foreach (var commentId in await db.KanbanCardComments.AsNoTracking()
                     .Select(comment => comment.Id)
                     .ToArrayAsync(cancellationToken))
        {
            if (!aliases.Comments.Values.Contains(commentId))
            {
                aliases.AddComment($"generated.comment-{commentId}", commentId);
            }
        }
    }

    private static Dictionary<TValue, string> Reverse<TValue>(
        IReadOnlyDictionary<string, TValue> aliases)
        where TValue : notnull => aliases.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static string RequiredAlias<TValue>(
        IReadOnlyDictionary<TValue, string> aliases,
        TValue id,
        string kind)
        where TValue : notnull
    {
        return aliases.TryGetValue(id, out var alias)
            ? alias
            : throw new InvalidOperationException($"Unmapped {kind} id '{id}'.");
    }

    private static string? Alias<TValue>(
        IReadOnlyDictionary<TValue, string> aliases,
        TValue? id)
        where TValue : notnull
    {
        if (id == null)
        {
            return null;
        }
        return aliases.TryGetValue(id, out var alias) ? alias : id.ToString();
    }
}

public sealed record KanbanExamStateSnapshot(
    IReadOnlyList<KanbanExamUserState> Users,
    IReadOnlyList<KanbanExamBoardState> Boards,
    IReadOnlyList<KanbanExamColumnState> Columns,
    IReadOnlyList<KanbanExamCardState> Cards,
    IReadOnlyList<KanbanExamShareState> Shares,
    IReadOnlyList<KanbanExamLabelState> Labels,
    IReadOnlyList<KanbanExamCardLabelState> CardLabels,
    IReadOnlyList<KanbanExamCommentState> Comments,
    IReadOnlyList<KanbanExamSubscriptionState> Subscriptions);

public sealed record KanbanExamUserState(string Id, string DisplayName, IReadOnlyList<string> Roles);
public sealed record KanbanExamBoardState(string Id, string Name, string? OwnerId, bool IsPublic, bool IsArchived, int Order);
public sealed record KanbanExamColumnState(string Id, string BoardId, string Name, string Status, int Order);
public sealed record KanbanExamCardState(string Id, string ColumnId, string Title, string Description, string? CreatorUserId, string? AssignedUserId, string Priority, DateTime? DueDate, int Order);
public sealed record KanbanExamShareState(string BoardId, string? UserId, string? RoleName, string Permission);
public sealed record KanbanExamLabelState(string Id, string Name, string Color);
public sealed record KanbanExamCardLabelState(string CardId, string LabelId);
public sealed record KanbanExamCommentState(string Id, string CardId, string AuthorUserId, string Content);
public sealed record KanbanExamSubscriptionState(string CardId, string UserId);
