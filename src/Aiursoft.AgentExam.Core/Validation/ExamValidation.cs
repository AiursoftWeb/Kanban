using System.Text.RegularExpressions;
using Aiursoft.AgentExam.Core.Models;

namespace Aiursoft.AgentExam.Core.Validation;

public static partial class ExamValidation
{
    public static void ValidateCandidate(ExamCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateSlug(candidate.Id, "candidate id");
        if (string.IsNullOrWhiteSpace(candidate.Endpoint) ||
            !Uri.TryCreate(candidate.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("candidate endpoint must be an absolute HTTP or HTTPS URI.", nameof(candidate));
        }
        if (string.IsNullOrWhiteSpace(candidate.Model))
        {
            throw new ArgumentException("candidate model is required.", nameof(candidate));
        }
        ValidateSlug(candidate.StrategyId, "candidate strategyId");
        if (candidate.Repetitions <= 0)
        {
            throw new ArgumentException("candidate repetitions must be positive.", nameof(candidate));
        }
    }

    public static string ResolveContainedPath(string rootDirectory, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(segments);

        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(Path.Combine([root, .. segments]));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) &&
            !string.Equals(candidate, root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Resolved path must remain inside the output directory.", nameof(segments));
        }
        return candidate;
    }

    public static void ValidateSlug(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !SlugRegex().IsMatch(value))
        {
            throw new ArgumentException(
                $"{field} must use lowercase letters, digits and single hyphen separators.",
                field);
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
