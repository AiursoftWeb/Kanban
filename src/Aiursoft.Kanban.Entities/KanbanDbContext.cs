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
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
        builder.Entity<KanbanCardComment>()
            .HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasOne(n => n.Card)
            .WithMany()
            .HasForeignKey(n => n.CardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Notification>()
            .HasOne(n => n.Comment)
            .WithMany()
            .HasForeignKey(n => n.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
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
