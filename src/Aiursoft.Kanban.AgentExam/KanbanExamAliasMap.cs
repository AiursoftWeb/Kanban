namespace Aiursoft.Kanban.Services.Agent.Exam;

public sealed class KanbanExamAliasMap
{
    private readonly Dictionary<string, string> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _boards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _columns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _comments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _roles = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Users => _users;
    public IReadOnlyDictionary<string, int> Boards => _boards;
    public IReadOnlyDictionary<string, int> Columns => _columns;
    public IReadOnlyDictionary<string, int> Cards => _cards;
    public IReadOnlyDictionary<string, int> Labels => _labels;
    public IReadOnlyDictionary<string, int> Comments => _comments;
    public IReadOnlyDictionary<string, string> Roles => _roles;

    public void AddUser(string alias, string id) => _users.Add(alias, id);
    public void AddBoard(string alias, int id) => _boards.Add(alias, id);
    public void AddColumn(string alias, int id) => _columns.Add(alias, id);
    public void AddCard(string alias, int id) => _cards.Add(alias, id);
    public void AddLabel(string alias, int id) => _labels.Add(alias, id);
    public void AddComment(string alias, int id) => _comments.Add(alias, id);
    public void AddRole(string name, string id) => _roles.TryAdd(name, id);

    public string GetUser(string alias) => Get(_users, alias, "user");
    public int GetBoard(string alias) => Get(_boards, alias, "board");
    public int GetColumn(string alias) => Get(_columns, alias, "column");
    public int GetCard(string alias) => Get(_cards, alias, "card");
    public int GetLabel(string alias) => Get(_labels, alias, "label");
    public string GetRole(string name) => Get(_roles, name, "role");

    private static TValue Get<TValue>(
        IReadOnlyDictionary<string, TValue> aliases,
        string alias,
        string kind)
    {
        return aliases.TryGetValue(alias, out var value)
            ? value
            : throw new InvalidOperationException($"Unknown {kind} alias '{alias}'.");
    }
}
