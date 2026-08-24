using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aiursoft.AgentExam.Core.Evaluation;

public static class JsonMatcher
{
    public static IReadOnlySet<string> SupportedOperators { get; } =
        new HashSet<string>(
            ["$exact", "$contains", "$regex", "$oneOf", "$subset", "$unorderedEquals", "$exists", "$var"],
            StringComparer.Ordinal);

    public static bool Matches(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? variables = null)
    {
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var props = expected.EnumerateObject().ToArray();
            if (props.Length == 1 && props[0].Name.StartsWith('$')) return MatchOperator(props[0], actual, variables);
            if (actual.ValueKind != JsonValueKind.Object) return false;
            return props.All(p => actual.TryGetProperty(p.Name, out var found) && Matches(p.Value, found, variables));
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            if (actual.ValueKind != JsonValueKind.Array) return false;
            var e = expected.EnumerateArray().ToArray(); var a = actual.EnumerateArray().ToArray();
            return e.Length == a.Length && e.Zip(a).All(x => Matches(x.First, x.Second, variables));
        }
        return expected.ValueKind == actual.ValueKind && expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => expected.GetRawText() == actual.GetRawText(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => expected.GetRawText() == actual.GetRawText()
        };
    }

    private static bool MatchOperator(JsonProperty op, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? vars) => op.Name switch
    {
        "$exact" => JsonElement.DeepEquals(op.Value, actual),
        "$contains" => Contains(op.Value, actual, vars),
        "$regex" => Regex.IsMatch(actual.ToString(), op.Value.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        "$oneOf" => op.Value.EnumerateArray().Any(x => Matches(x, actual, vars)),
        "$subset" => op.Value.EnumerateArray().All(e => actual.ValueKind == JsonValueKind.Array && actual.EnumerateArray().Any(a => Matches(e, a, vars))),
        "$unorderedEquals" => Unordered(op.Value, actual, vars),
        "$exists" => op.Value.GetBoolean() == (actual.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined),
        "$var" => vars != null && vars.TryGetValue(op.Value.GetString()!, out var v) && Matches(v, actual, vars),
        _ => false
    };

    private static bool Contains(
        JsonElement expected,
        JsonElement actual,
        IReadOnlyDictionary<string, JsonElement>? variables)
    {
        return actual.ValueKind switch
        {
            JsonValueKind.String when expected.ValueKind == JsonValueKind.String =>
                actual.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => actual.EnumerateArray().Any(item => Matches(expected, item, variables)),
            JsonValueKind.Object when expected.ValueKind == JsonValueKind.Object =>
                Matches(expected, actual, variables),
            _ => false
        };
    }

    private static bool Unordered(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? vars)
    {
        if (expected.ValueKind != JsonValueKind.Array || actual.ValueKind != JsonValueKind.Array) return false;
        var remaining = actual.EnumerateArray().ToList();
        foreach (var item in expected.EnumerateArray()) { var i = remaining.FindIndex(x => Matches(item, x, vars)); if (i < 0) return false; remaining.RemoveAt(i); }
        return remaining.Count == 0;
    }
}
