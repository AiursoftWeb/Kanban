using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Kanban.Services.Auditing;

public class AuditLogContext : IScopedDependency
{
    public bool HasSemanticLog { get; set; }
}
