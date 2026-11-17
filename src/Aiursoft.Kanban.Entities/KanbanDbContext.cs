using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<PersonalApiKey> PersonalApiKeys => Set<PersonalApiKey>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<KanbanBoard> KanbanBoards => Set<KanbanBoard>();
    public DbSet<KanbanColumn> KanbanColumns => Set<KanbanColumn>();
    public DbSet<KanbanCategory> KanbanCategories => Set<KanbanCategory>();
    public DbSet<KanbanCard> KanbanCards => Set<KanbanCard>();
    public DbSet<KanbanLabel> KanbanLabels => Set<KanbanLabel>();
    public DbSet<KanbanCardLabel> KanbanCardLabels => Set<KanbanCardLabel>();
    public DbSet<BoardShare> BoardShares => Set<BoardShare>();
    public DbSet<KanbanCardComment> KanbanCardComments => Set<KanbanCardComment>();
    public DbSet<KanbanCardSubscription> KanbanCardSubscriptions => Set<KanbanCardSubscription>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DailyReport> DailyReports => Set<DailyReport>();
    public DbSet<WeeklyReport> WeeklyReports => Set<WeeklyReport>();
    public DbSet<SearchEmbedding> SearchEmbeddings => Set<SearchEmbedding>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await PrepareCategoriesAsync(cancellationToken);
        var entries = ChangeTracker.Entries<KanbanCard>()
            .Where(e => e.State == EntityState.Modified &&
                (e.Property(c => c.Title).IsModified || e.Property(c => c.Description).IsModified));

        foreach (var entry in entries)
        {
            entry.Entity.LastUpdatedAt = DateTime.UtcNow;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task PrepareCategoriesAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in ChangeTracker.Entries<KanbanBoard>().Where(e => e.State == EntityState.Added).ToList())
        {
            if (!entry.Entity.Categories.Any(c => c.IsDefault))
            {
                KanbanCategories.Add(new KanbanCategory { Name = "Default", IsDefault = true, Board = entry.Entity });
            }
        }

        var cards = ChangeTracker.Entries<KanbanCard>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified &&
                (e.Property(c => c.CategoryId).IsModified || e.Property(c => c.ColumnId).IsModified))
            .Select(e => e.Entity).ToList();
        foreach (var card in cards)
        {
            var column = Entry(card).Reference(c => c.Column).TargetEntry?.Entity ?? await KanbanColumns.FindAsync([card.ColumnId], cancellationToken)
                ?? throw new InvalidOperationException("Card column not found.");
            if (card.CategoryId == 0 && Entry(card).Reference(c => c.Category).TargetEntry == null)
            {
                card.Category = Entry(column).Reference(c => c.Board).TargetEntry?.Entity.Categories.FirstOrDefault(c => c.IsDefault)
                    ?? KanbanCategories.Local.FirstOrDefault(c => c.IsDefault && c.BoardId == column.BoardId)
                    ?? await KanbanCategories.SingleAsync(c => c.BoardId == column.BoardId && c.IsDefault, cancellationToken);
            }
            var category = Entry(card).Reference(c => c.Category).TargetEntry?.Entity ?? await KanbanCategories.FindAsync([card.CategoryId], cancellationToken)
                ?? throw new InvalidOperationException("Card category not found.");
            if (category.BoardId != column.BoardId ||
                category.BoardId == 0 && !ReferenceEquals(category.Board, column.Board))
            {
                throw new InvalidOperationException("The category and status column must belong to the same board.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<KanbanBoard>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PersonalApiKey>().HasIndex(key => key.KeyHash).IsUnique();
        builder.Entity<PersonalApiKey>().HasOne(key => key.User).WithMany()
            .HasForeignKey(key => key.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<KanbanCategory>().HasIndex(category => new { category.BoardId, category.Name }).IsUnique();
        builder.Entity<KanbanCard>().HasOne(card => card.Category).WithMany(category => category.Cards)
            .HasForeignKey(card => card.CategoryId).OnDelete(DeleteBehavior.Cascade);

        ConfigureKanbanCardLabel(builder.Entity<KanbanCardLabel>());
        builder.Entity<KanbanCard>()
            .HasOne(card => card.AssignedUser)
            .WithMany()
            .HasForeignKey(card => card.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<KanbanCard>()
            .HasOne(card => card.CreatorUser)
            .WithMany()
            .HasForeignKey(card => card.CreatorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<KanbanCard>()
            .Property(card => card.Priority)
            .HasDefaultValue(Priority.None);
        builder.Entity<KanbanLabel>()
            .HasIndex(label => label.Name)
            .IsUnique();
        builder.Entity<KanbanCardComment>()
            .HasOne(comment => comment.Card)
            .WithMany()
            .HasForeignKey(comment => comment.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<KanbanCardSubscription>()
            .HasKey(subscription => new { subscription.CardId, subscription.UserId });
        builder.Entity<KanbanCardSubscription>()
            .HasOne(subscription => subscription.Card)
            .WithMany(card => card.Subscriptions)
            .HasForeignKey(subscription => subscription.CardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<KanbanCardSubscription>()
            .HasOne(subscription => subscription.User)
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<KanbanCardComment>()
            .HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasOne(n => n.Card)
            .WithMany()
            .HasForeignKey(n => n.CardId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
        builder.Entity<Notification>()
            .HasOne(n => n.Comment)
            .WithMany()
            .HasForeignKey(n => n.CommentId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
        builder.Entity<Notification>()
            .HasOne(n => n.Board)
            .WithMany()
            .HasForeignKey(n => n.BoardId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
        builder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Notification>()
            .HasOne(n => n.ActorUser)
            .WithMany()
            .HasForeignKey(n => n.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.Entity<DailyReport>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DailyReport>()
            .HasIndex(r => new { r.UserId, r.Date, r.ReportType })
            .IsUnique();

        builder.Entity<WeeklyReport>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AgentSession>()
            .HasOne(session => session.User)
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AgentSession>()
            .HasOne(session => session.Board)
            .WithMany()
            .HasForeignKey(session => session.BoardId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
        builder.Entity<AgentSession>()
            .HasIndex(session => new { session.UserId, session.LastActivity });
        builder.Entity<WeeklyReport>()
            .HasIndex(r => new { r.UserId, r.WeekStart })
            .IsUnique();
    }

    public virtual Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();

    private static void ConfigureKanbanCardLabel(EntityTypeBuilder<KanbanCardLabel> builder)
    {
        builder.HasKey(link => new { link.CardId, link.LabelId });
        builder.HasOne(link => link.Card)
            .WithMany(card => card.CardLabels)
            .HasForeignKey(link => link.CardId);
        builder.HasOne(link => link.Label)
            .WithMany(label => label.CardLabels)
            .HasForeignKey(link => link.LabelId);
    }
}
