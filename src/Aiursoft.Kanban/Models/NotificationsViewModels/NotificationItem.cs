namespace Aiursoft.Kanban.Models.NotificationsViewModels;

public class NotificationItem
{
    public int Id { get; set; }
    public int CardId { get; set; }
    public int BoardId { get; set; }
    public required string CardTitle { get; set; }
    public required string BoardName { get; set; }
    public required string ColumnName { get; set; }
    public required string CommentContent { get; set; }
    public string? CommentAuthorName { get; set; }
    public string CommentAuthorInitial { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}
