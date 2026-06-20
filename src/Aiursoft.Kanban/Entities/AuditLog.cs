namespace Aiursoft.Kanban.Entities;

public class AuditLog
{
    public DateTime EventTime { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}
