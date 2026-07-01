namespace Aiursoft.Kanban.Services.Auditing;

public static class AuditDetailFilter
{
    private static readonly string[] SensitiveNames =
        ["password", "token", "secret", "content", "description", "json"];

    public static bool IsSensitiveName(string name)
    {
        return SensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }

    public static Dictionary<string, object?> ToSafeDictionary(IReadOnlyDictionary<string, object?> details)
    {
        return details
            .Where(pair => !IsSensitiveName(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}
