using System.Text.Json;

namespace Aiursoft.AgentExam.Core.Evaluation;

/// <summary>Compatibility facade for the shared JSON matching engine.</summary>
public static class JsonMatcher
{
    public static IReadOnlySet<string> SupportedOperators => Aiursoft.AgentKit.Evaluator.JsonMatcher.SupportedOperators;

    public static bool Matches(
        JsonElement expected,
        JsonElement actual,
        IReadOnlyDictionary<string, JsonElement>? variables = null) =>
        Aiursoft.AgentKit.Evaluator.JsonMatcher.Matches(expected, actual, variables);
}
