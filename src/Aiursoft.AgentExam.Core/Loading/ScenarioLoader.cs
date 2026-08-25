using System.Text.Json;
using System.Text.RegularExpressions;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Evaluation;
using Aiursoft.AgentExam.Core.Models;

namespace Aiursoft.AgentExam.Core.Loading;

public sealed class ScenarioValidationException(string message) : Exception(message);

public sealed partial class ScenarioLoader(IReadOnlySet<string>? knownTools = null) : IScenarioLoader
{
    private static readonly IReadOnlySet<string> ColumnStatuses =
        new HashSet<string>(["NotStarted", "InProgress", "Completed"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> SharePermissions =
        new HashSet<string>(["ReadOnly", "Editable"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CardPriorities =
        new HashSet<string>(["Urgent", "High", "Medium", "Low", "None"], StringComparer.Ordinal);

    public Task<IReadOnlyList<ExamScenario>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        LoadAsync([path], cancellationToken);

    public async Task<IReadOnlyList<ExamScenario>> LoadAsync(
        IEnumerable<string> patterns,
        CancellationToken cancellationToken = default)
    {
        var requested = patterns.ToArray();
        var files = requested
            .SelectMany(ExpandPattern)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new ScenarioValidationException(
                $"No scenario JSON files matched: {string.Join(", ", requested)}.");
        }

        var scenarios = new List<ExamScenario>();
        foreach (var file in files)
        {
            try
            {
                await using var stream = File.OpenRead(file);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                var roots = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray().ToArray()
                    : [document.RootElement];
                foreach (var root in roots)
                {
                    ValidateRequiredJson(root);
                    var scenario = root.Deserialize<ExamScenario>(JsonDefaults.Options) ??
                        throw new ScenarioValidationException("Document is empty.");
                    Validate(scenario);
                    scenarios.Add(scenario);
                }
            }
            catch (Exception exception) when (
                exception is JsonException or ScenarioValidationException)
            {
                throw new ScenarioValidationException($"{file}: {exception.Message}");
            }
        }

        var duplicate = scenarios
            .GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new ScenarioValidationException(
                $"Duplicate scenario id '{duplicate.Key}'.");
        }

        return scenarios;
    }

    private static void ValidateRequiredJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioValidationException("Each scenario must be a JSON object.");
        }

        RequireProperty(root, "schemaVersion");
        RequireProperty(root, "id");
        RequireProperty(root, "name");
        RequireProperty(root, "domain");
        RequireProperty(root, "fixedUtcNow");
        var setup = RequireProperty(root, "setup");
        var steps = RequireProperty(root, "steps");
        if (setup.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioValidationException("setup must be an object.");
        }
        RequireArrayProperty(setup, "users");
        RequireArrayProperty(setup, "boards");
        RequireArrayProperty(setup, "columns");
        RequireArrayProperty(setup, "shares");
        RequireArrayProperty(setup, "cards");
        ValidateOptionalArrayProperty(setup, "labels");
        ValidateOptionalArrayProperty(setup, "comments");
        ValidateOptionalArrayProperty(setup, "subscriptions");
        if (steps.ValueKind != JsonValueKind.Array)
        {
            throw new ScenarioValidationException("steps must be an array.");
        }
        foreach (var (step, index) in steps.EnumerateArray().Select((value, index) => (value, index)))
        {
            if (step.ValueKind != JsonValueKind.Object)
            {
                throw new ScenarioValidationException($"Step {index} must be an object.");
            }
            RequireProperty(step, "userId");
            RequireProperty(step, "boardId");
            RequireProperty(step, "userMessage");
            var expect = RequireProperty(step, "expect");
            if (expect.ValueKind != JsonValueKind.Object)
            {
                throw new ScenarioValidationException($"Step {index} expect must be an object.");
            }
            RequireArrayProperty(expect, "trace");
            RequireArrayProperty(expect, "state");
            RequireArrayProperty(expect, "response");
        }
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            throw new ScenarioValidationException($"Required property '{name}' is missing.");
        }

        return value;
    }

    private static JsonElement RequireArrayProperty(JsonElement element, string name)
    {
        var value = RequireProperty(element, name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ScenarioValidationException($"{name} must be an array.");
        }
        return value;
    }

    private static void ValidateOptionalArrayProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Array)
        {
            throw new ScenarioValidationException($"{name} must be an array.");
        }
    }

    private void Validate(ExamScenario scenario)
    {
        if (scenario.SchemaVersion != "1.0")
        {
            throw new ScenarioValidationException(
                $"Unsupported schemaVersion '{scenario.SchemaVersion}'.");
        }
        ValidateSlug(scenario.Id, "id");
        ValidateSlug(scenario.Domain, "domain");
        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            throw new ScenarioValidationException("name is required.");
        }
        if (scenario.FixedUtcNow == default || scenario.FixedUtcNow.Offset != TimeSpan.Zero)
        {
            throw new ScenarioValidationException("fixedUtcNow must be a non-default UTC timestamp.");
        }
        if (!double.IsFinite(scenario.Weight) || scenario.Weight <= 0 || scenario.TimeoutSeconds <= 0)
        {
            throw new ScenarioValidationException("weight and timeoutSeconds must be positive.");
        }
        if (scenario.Steps.Length == 0)
        {
            throw new ScenarioValidationException("steps must be non-empty.");
        }

        ValidateSetup(scenario.Setup);
        var userIds = scenario.Setup.Users.Select(user => user.Id).ToHashSet(StringComparer.Ordinal);
        var boardIds = scenario.Setup.Boards.Select(board => board.Id).ToHashSet(StringComparer.Ordinal);
        var assertionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (step, stepIndex) in scenario.Steps.Select((step, index) => (step, index)))
        {
            if (string.IsNullOrWhiteSpace(step.UserId) ||
                string.IsNullOrWhiteSpace(step.BoardId) ||
                string.IsNullOrWhiteSpace(step.UserMessage))
            {
                throw new ScenarioValidationException(
                    $"Step {stepIndex} requires userId, boardId and userMessage.");
            }
            if (!userIds.Contains(step.UserId))
            {
                throw new ScenarioValidationException(
                    $"Step {stepIndex} userId references unknown user '{step.UserId}'.");
            }
            if (!boardIds.Contains(step.BoardId))
            {
                throw new ScenarioValidationException(
                    $"Step {stepIndex} boardId references unknown board '{step.BoardId}'.");
            }

            foreach (var assertion in step.Expect.Trace
                         .Concat(step.Expect.State)
                         .Concat(step.Expect.Response))
            {
                ValidateAssertion(assertion, assertionIds);
            }
        }
    }

    private void ValidateAssertion(AssertionSpec assertion, HashSet<string> assertionIds)
    {
        ValidateSlug(assertion.Id, "assertion id");
        if (!assertionIds.Add(assertion.Id))
        {
            throw new ScenarioValidationException(
                $"Duplicate assertion id '{assertion.Id}'.");
        }
        if (!AssertionKinds.All.Contains(assertion.Kind))
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' has unknown kind '{assertion.Kind}'.");
        }
        if (!double.IsFinite(assertion.Points) || !double.IsFinite(assertion.Penalty) ||
            assertion.Points < 0 || assertion.Penalty > 0 ||
            (assertion.Points == 0 && assertion.Penalty == 0))
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' needs non-negative points or a negative penalty.");
        }

        if (assertion.Kind is AssertionKinds.Tool or AssertionKinds.ForbidTool)
        {
            ValidateToolMatch(assertion);
        }
        else if (assertion.Kind is AssertionKinds.MaxToolCalls or AssertionKinds.MaxLoops)
        {
            if (assertion.Match.ValueKind != JsonValueKind.Number ||
                !assertion.Match.TryGetInt32(out var maximum) || maximum < 0)
            {
                throw new ScenarioValidationException(
                    $"Assertion '{assertion.Id}' match must be a non-negative integer.");
            }
        }
        else if (assertion.Match.ValueKind == JsonValueKind.Undefined)
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' requires match.");
        }

        ValidateMatchers(assertion.Match, assertion.Id);
    }

    private void ValidateToolMatch(AssertionSpec assertion)
    {
        if (assertion.Match.ValueKind != JsonValueKind.Object ||
            !assertion.Match.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' requires match.name.");
        }

        var toolName = nameElement.GetString()!;
        if (knownTools != null && !knownTools.Contains(toolName))
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' references unknown tool '{toolName}'.");
        }
        ValidateOptionalNonNegativeInteger(assertion, "minCount");
        ValidateOptionalNonNegativeInteger(assertion, "maxCount");
        if (assertion.Match.TryGetProperty("minCount", out var min) &&
            assertion.Match.TryGetProperty("maxCount", out var max) &&
            min.GetInt32() > max.GetInt32())
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' minCount cannot exceed maxCount.");
        }
    }

    private static void ValidateOptionalNonNegativeInteger(AssertionSpec assertion, string name)
    {
        if (assertion.Match.TryGetProperty(name, out var value) &&
            (value.ValueKind != JsonValueKind.Number ||
             !value.TryGetInt32(out var parsed) || parsed < 0))
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertion.Id}' {name} must be a non-negative integer.");
        }
    }

    private static void ValidateSetup(ExamSetup setup)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in setup.Users)
        {
            AddAlias(aliases, user.Id);
            RequireText(user.DisplayName, $"User '{user.Id}' displayName");
            foreach (var role in user.Roles)
            {
                RequireText(role, $"User '{user.Id}' role");
            }
        }
        foreach (var board in setup.Boards)
        {
            AddAlias(aliases, board.Id);
            RequireText(board.Name, $"Board '{board.Id}' name");
            RequireReference(aliases, board.OwnerId, $"Board '{board.Id}' ownerId");
        }
        foreach (var column in setup.Columns)
        {
            AddAlias(aliases, column.Id);
            RequireReference(aliases, column.BoardId, $"Column '{column.Id}' boardId");
            RequireText(column.Name, $"Column '{column.Id}' name");
            RequireEnum(ColumnStatuses, column.Status, $"Column '{column.Id}' status");
        }
        foreach (var share in setup.Shares)
        {
            RequireReference(aliases, share.BoardId, "Share boardId");
            var hasUser = !string.IsNullOrWhiteSpace(share.UserId);
            var hasRole = !string.IsNullOrWhiteSpace(share.RoleName);
            if (hasUser == hasRole)
            {
                throw new ScenarioValidationException(
                    "Each share requires exactly one of userId or roleName.");
            }
            if (hasUser)
            {
                RequireReference(aliases, share.UserId!, "Share userId");
            }
            else
            {
                RequireText(share.RoleName!, "Share roleName");
            }
            RequireEnum(SharePermissions, share.Permission, "Share permission");
        }
        foreach (var card in setup.Cards)
        {
            AddAlias(aliases, card.Id);
            RequireReference(aliases, card.ColumnId, $"Card '{card.Id}' columnId");
            RequireReference(aliases, card.CreatorUserId, $"Card '{card.Id}' creatorUserId");
            if (!string.IsNullOrWhiteSpace(card.AssignedUserId))
            {
                RequireReference(aliases, card.AssignedUserId, $"Card '{card.Id}' assignedUserId");
            }
            RequireText(card.Title, $"Card '{card.Id}' title");
            RequireEnum(CardPriorities, card.Priority, $"Card '{card.Id}' priority");
        }
        foreach (var label in setup.Labels)
        {
            AddAlias(aliases, label.Id);
            RequireText(label.Name, $"Label '{label.Id}' name");
            if (!ColorRegex().IsMatch(label.Color))
            {
                throw new ScenarioValidationException(
                    $"Label '{label.Id}' color must use #RRGGBB format.");
            }
            foreach (var cardId in label.CardIds)
            {
                RequireReference(aliases, cardId, $"Label '{label.Id}' cardId");
            }
        }
        foreach (var comment in setup.Comments)
        {
            AddAlias(aliases, comment.Id);
            RequireReference(aliases, comment.CardId, $"Comment '{comment.Id}' cardId");
            RequireReference(aliases, comment.AuthorUserId, $"Comment '{comment.Id}' authorUserId");
            RequireText(comment.Content, $"Comment '{comment.Id}' content");
        }
        foreach (var subscription in setup.Subscriptions)
        {
            RequireReference(aliases, subscription.CardId, "Subscription cardId");
            RequireReference(aliases, subscription.UserId, "Subscription userId");
        }
    }

    private static void AddAlias(HashSet<string> aliases, string alias)
    {
        RequireText(alias, "Setup alias");
        if (!AliasRegex().IsMatch(alias))
        {
            throw new ScenarioValidationException(
                $"Setup alias '{alias}' must contain lowercase dot-separated segments.");
        }
        if (!aliases.Add(alias))
        {
            throw new ScenarioValidationException($"Duplicate setup alias '{alias}'.");
        }
    }

    private static void RequireReference(HashSet<string> aliases, string alias, string field)
    {
        if (!aliases.Contains(alias))
        {
            throw new ScenarioValidationException(
                $"{field} references unknown or not-yet-declared alias '{alias}'.");
        }
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ScenarioValidationException($"{field} is required.");
        }
    }

    private static void RequireEnum(IReadOnlySet<string> values, string value, string field)
    {
        if (!values.Contains(value))
        {
            throw new ScenarioValidationException(
                $"{field} has unsupported value '{value}'.");
        }
    }

    private static void ValidateSlug(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !SlugRegex().IsMatch(value))
        {
            throw new ScenarioValidationException(
                $"{field} must use lowercase letters, digits and single hyphen separators.");
        }
    }

    private static void ValidateMatchers(JsonElement value, string assertionId)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            var operators = properties.Where(property => property.Name.StartsWith('$')).ToArray();
            if (operators.Length > 0)
            {
                if (properties.Length != 1 || !JsonMatcher.SupportedOperators.Contains(operators[0].Name))
                {
                    throw new ScenarioValidationException(
                        $"Assertion '{assertionId}' has an unknown or mixed matcher operator.");
                }
                ValidateOperator(operators[0], assertionId);
            }
            foreach (var property in properties)
            {
                ValidateMatchers(property.Value, assertionId);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                ValidateMatchers(item, assertionId);
            }
        }
    }

    private static void ValidateOperator(JsonProperty property, string assertionId)
    {
        try
        {
            switch (property.Name)
            {
                case "$regex":
                    _ = new Regex(
                        property.Value.GetString() ?? throw new InvalidOperationException(),
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1));
                    break;
                case "$oneOf" or "$subset" or "$unorderedEquals" when
                    property.Value.ValueKind != JsonValueKind.Array:
                    throw new InvalidOperationException("operator value must be an array");
                case "$exists" when property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False:
                    throw new InvalidOperationException("operator value must be boolean");
                case "$var" when property.Value.ValueKind != JsonValueKind.String:
                    throw new InvalidOperationException("operator value must be a string");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ScenarioValidationException(
                $"Assertion '{assertionId}' has invalid {property.Name}: {exception.Message}");
        }
    }

    private static IEnumerable<string> ExpandPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            yield break;
        }
        if (Directory.Exists(pattern))
        {
            foreach (var file in Directory.EnumerateFiles(
                         pattern,
                         "*.json",
                         SearchOption.AllDirectories))
            {
                yield return file;
            }
            yield break;
        }
        if (!HasWildcard(pattern))
        {
            if (File.Exists(pattern))
            {
                yield return pattern;
            }
            yield break;
        }

        var fullPattern = Path.GetFullPath(pattern).Replace('\\', '/');
        var wildcardIndex = fullPattern.IndexOfAny(['*', '?']);
        var slashIndex = fullPattern.LastIndexOf('/', wildcardIndex);
        var searchRoot = slashIndex <= 0
            ? Path.GetPathRoot(fullPattern)!
            : fullPattern[..slashIndex];
        if (!Directory.Exists(searchRoot))
        {
            yield break;
        }
        var regex = GlobToRegex(fullPattern);
        foreach (var file in Directory.EnumerateFiles(
                     searchRoot,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            if (regex.IsMatch(Path.GetFullPath(file).Replace('\\', '/')))
            {
                yield return file;
            }
        }
    }

    private static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;

    private static Regex GlobToRegex(string pattern)
    {
        var builder = new System.Text.StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                {
                    builder.Append("(?:.*/)?");
                    index += 2;
                }
                else
                {
                    builder.Append(".*");
                    index++;
                }
            }
            else if (current == '*')
            {
                builder.Append("[^/]*");
            }
            else if (current == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(current.ToString()));
            }
        }
        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant);
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$")]
    private static partial Regex AliasRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorRegex();
}
