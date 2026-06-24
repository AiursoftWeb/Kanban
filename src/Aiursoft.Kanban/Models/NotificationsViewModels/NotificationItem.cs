using Aiursoft.Kanban.Entities;

namespace Aiursoft.Kanban.Models.NotificationsViewModels;

public class NotificationItem
{
    public int Id { get; set; }
    public int? CardId { get; set; }
    public int? BoardId { get; set; }
    public string? CardTitle { get; set; }
    public string? BoardName { get; set; }
    public string? ColumnName { get; set; }
    public string? CommentContent { get; set; }
    public string? CommentAuthorName { get; set; }
    public string CommentAuthorInitial { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ActorUserName { get; set; }
    public DateTime CreationTime { get; set; }
}
