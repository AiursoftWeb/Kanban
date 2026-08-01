using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aiursoft.Kanban.Entities;

[ExcludeFromCodeCoverage]

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<KanbanBoard> KanbanBoards => Set<KanbanBoard>();
    public DbSet<KanbanColumn> KanbanColumns => Set<KanbanColumn>();
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

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<KanbanCard>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            entry.Entity.LastUpdatedAt = DateTime.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<KanbanBoard>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

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
