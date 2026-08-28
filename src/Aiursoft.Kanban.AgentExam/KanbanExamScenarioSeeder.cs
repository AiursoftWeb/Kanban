using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Kanban.AgentExam;

public sealed class KanbanExamScenarioSeeder(
    TemplateDbContext db,
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    TimeProvider timeProvider)
{
    public async Task<KanbanExamAliasMap> SeedAsync(
        ExamSetup setup,
        CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var aliases = new KanbanExamAliasMap();
        try
        {
            await SeedUsersAndRoles(setup, aliases);
            await SeedBoards(setup, aliases, cancellationToken);
            await SeedColumns(setup, aliases, cancellationToken);
            await SeedShares(setup, aliases, cancellationToken);
            await SeedCards(setup, aliases, cancellationToken);
            await SeedLabels(setup, aliases, cancellationToken);
            await SeedComments(setup, aliases, cancellationToken);
            await SeedSubscriptions(setup, aliases, cancellationToken);
            return aliases;
        }
        catch
        {
            db.ChangeTracker.Clear();
            await db.Database.EnsureDeletedAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            throw;
        }
    }

    private async Task SeedUsersAndRoles(ExamSetup setup, KanbanExamAliasMap aliases)
    {
        foreach (var setupUser in setup.Users)
        {
            var user = new User
            {
                Id = CreateStableIdentityId(setupUser.Id),
                UserName = $"{setupUser.Id}@exam.invalid",
                Email = $"{setupUser.Id}@exam.invalid",
                DisplayName = setupUser.DisplayName,
                EmailConfirmed = true
            };
            var createResult = await userManager.CreateAsync(user);
            EnsureIdentitySucceeded(createResult, $"create user '{setupUser.Id}'");
            aliases.AddUser(setupUser.Id, user.Id);

            foreach (var roleName in setupUser.Roles.Distinct(StringComparer.Ordinal))
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var role = new IdentityRole(roleName)
                    {
                        Id = CreateStableIdentityId($"role:{roleName}")
                    };
                    EnsureIdentitySucceeded(
                        await roleManager.CreateAsync(role),
                        $"create role '{roleName}'");
                    aliases.AddRole(roleName, role.Id);
                }
                else if (!aliases.Roles.ContainsKey(roleName))
                {
                    var role = await roleManager.FindByNameAsync(roleName) ??
                        throw new InvalidOperationException($"Role '{roleName}' was not found.");
                    aliases.AddRole(roleName, role.Id);
                }
                EnsureIdentitySucceeded(
                    await userManager.AddToRoleAsync(user, roleName),
                    $"add user '{setupUser.Id}' to role '{roleName}'");
            }
        }
    }

    private async Task SeedBoards(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupBoard in setup.Boards)
        {
            var board = new KanbanBoard
            {
                Name = setupBoard.Name,
                UserId = aliases.GetUser(setupBoard.OwnerId),
                IsPublic = setupBoard.IsPublic
            };
            db.KanbanBoards.Add(board);
            await db.SaveChangesAsync(cancellationToken);
            aliases.AddBoard(setupBoard.Id, board.Id);
        }
    }

    private async Task SeedColumns(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupColumn in setup.Columns)
        {
            var column = new KanbanColumn
            {
                Name = setupColumn.Name,
                BoardId = aliases.GetBoard(setupColumn.BoardId),
                ColumnStatus = Enum.Parse<ColumnStatus>(setupColumn.Status),
                Order = setupColumn.Order
            };
            db.KanbanColumns.Add(column);
            await db.SaveChangesAsync(cancellationToken);
            aliases.AddColumn(setupColumn.Id, column.Id);
        }
    }

    private async Task SeedShares(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupShare in setup.Shares)
        {
            if (setupShare.RoleName != null && !aliases.Roles.ContainsKey(setupShare.RoleName))
            {
                var role = new IdentityRole(setupShare.RoleName)
                {
                    Id = CreateStableIdentityId($"role:{setupShare.RoleName}")
                };
                EnsureIdentitySucceeded(
                    await roleManager.CreateAsync(role),
                    $"create role '{setupShare.RoleName}'");
                aliases.AddRole(setupShare.RoleName, role.Id);
            }

            db.BoardShares.Add(new BoardShare
            {
                Id = Guid.NewGuid(),
                BoardId = aliases.GetBoard(setupShare.BoardId),
                SharedWithUserId = setupShare.UserId == null
                    ? null
                    : aliases.GetUser(setupShare.UserId),
                SharedWithRoleId = setupShare.RoleName == null
                    ? null
                    : aliases.GetRole(setupShare.RoleName),
                Permission = Enum.Parse<SharePermission>(setupShare.Permission)
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedCards(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupCard in setup.Cards)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var card = new KanbanCard
            {
                Title = setupCard.Title,
                Description = setupCard.Description,
                ColumnId = aliases.GetColumn(setupCard.ColumnId),
                CreatorUserId = aliases.GetUser(setupCard.CreatorUserId),
                AssignedUserId = setupCard.AssignedUserId == null
                    ? null
                    : aliases.GetUser(setupCard.AssignedUserId),
                Priority = Enum.Parse<Priority>(setupCard.Priority),
                DueDate = setupCard.DueDate?.UtcDateTime,
                Order = setupCard.Order,
                CreationTime = now,
                LastUpdatedAt = now
            };
            db.KanbanCards.Add(card);
            await db.SaveChangesAsync(cancellationToken);
            aliases.AddCard(setupCard.Id, card.Id);
        }
    }

    private async Task SeedLabels(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupLabel in setup.Labels)
        {
            var label = new KanbanLabel
            {
                Name = setupLabel.Name,
                Color = setupLabel.Color
            };
            db.KanbanLabels.Add(label);
            await db.SaveChangesAsync(cancellationToken);
            aliases.AddLabel(setupLabel.Id, label.Id);
            foreach (var cardAlias in setupLabel.CardIds)
            {
                db.KanbanCardLabels.Add(new KanbanCardLabel
                {
                    CardId = aliases.GetCard(cardAlias),
                    LabelId = label.Id
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedComments(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var setupComment in setup.Comments)
        {
            var comment = new KanbanCardComment
            {
                CardId = aliases.GetCard(setupComment.CardId),
                AuthorId = aliases.GetUser(setupComment.AuthorUserId),
                Content = setupComment.Content,
                CreationTime = timeProvider.GetUtcNow().UtcDateTime
            };
            db.KanbanCardComments.Add(comment);
            await db.SaveChangesAsync(cancellationToken);
            aliases.AddComment(setupComment.Id, comment.Id);
        }
    }

    private async Task SeedSubscriptions(
        ExamSetup setup,
        KanbanExamAliasMap aliases,
        CancellationToken cancellationToken)
    {
        foreach (var subscription in setup.Subscriptions)
        {
            db.KanbanCardSubscriptions.Add(new KanbanCardSubscription
            {
                CardId = aliases.GetCard(subscription.CardId),
                UserId = aliases.GetUser(subscription.UserId),
                CreationTime = timeProvider.GetUtcNow().UtcDateTime
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateStableIdentityId(string alias) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(alias))).ToLowerInvariant();

    private static void EnsureIdentitySucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
