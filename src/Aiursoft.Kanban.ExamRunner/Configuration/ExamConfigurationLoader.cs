using System.Text.Json;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Validation;
using Aiursoft.Kanban.Services.Agent;

namespace Aiursoft.Kanban.ExamRunner.Configuration;

public static class ExamConfigurationLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options)
    {
        AllowTrailingCommas = true
    };

    public static async Task<LoadedExamConfiguration> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var configurationPath = Path.GetFullPath(path);
        var configurationDirectory = Path.GetDirectoryName(configurationPath) ??
            throw new InvalidOperationException("Configuration path has no parent directory.");
        var json = await File.ReadAllTextAsync(configurationPath, cancellationToken);
        var configuration = JsonSerializer.Deserialize<ExamConfiguration>(json, Options) ??
            throw new InvalidOperationException("Configuration is empty.");
        ValidateConfiguration(configuration);

        var scenarioPatterns = configuration.Scenarios
            .Select(pattern => ResolveRelativePath(configurationDirectory, pattern, "scenario path"))
            .ToArray();
        var outputDirectory = ResolveRelativePath(
            configurationDirectory,
            configuration.OutputDirectory,
            "output directory");
        var candidates = new List<LoadedCandidate>();
        foreach (var candidate in configuration.Candidates)
        {
            string? prompt = candidate.Prompt;
            if (candidate.PromptFile != null)
            {
                var promptPath = ResolveRelativePath(
                    configurationDirectory,
                    candidate.PromptFile,
                    $"candidate '{candidate.Id}' prompt file");
                prompt = await File.ReadAllTextAsync(promptPath, cancellationToken);
            }

            var examCandidate = candidate.ToExamCandidate(prompt);
            ExamValidation.ValidateCandidate(examCandidate);
            ValidateTools(examCandidate);
            var credential = ResolveCredential(candidate.Id, candidate.Authentication);
            candidates.Add(new LoadedCandidate(
                examCandidate,
                candidate.Authentication,
                credential,
                prompt));
        }

        return new LoadedExamConfiguration(
            configuration,
            configurationDirectory,
            scenarioPatterns,
            outputDirectory,
            candidates);
    }

    private static void ValidateConfiguration(ExamConfiguration configuration)
    {
        if (configuration.SchemaVersion != "1.0")
        {
            throw new InvalidOperationException(
                $"Unsupported configuration schemaVersion '{configuration.SchemaVersion}'.");
        }
        if (configuration.Scenarios.Length == 0)
        {
            throw new InvalidOperationException("At least one scenario path is required.");
        }
        if (configuration.Scenarios.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Scenario paths cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(configuration.OutputDirectory))
        {
            throw new InvalidOperationException("outputDirectory is required.");
        }
        if (configuration.FailBelow is < 0 or > 100)
        {
            throw new InvalidOperationException("failBelow must be between 0 and 100.");
        }
        if (configuration.Candidates.Length == 0)
        {
            throw new InvalidOperationException("At least one candidate is required.");
        }
        var duplicate = configuration.Candidates
            .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new InvalidOperationException($"Duplicate candidate id '{duplicate.Key}'.");
        }
        foreach (var candidate in configuration.Candidates)
        {
            if (candidate.Prompt != null && string.IsNullOrWhiteSpace(candidate.Prompt))
            {
                throw new InvalidOperationException($"Candidate '{candidate.Id}' prompt cannot be empty.");
            }
            if (candidate.PromptFile != null && string.IsNullOrWhiteSpace(candidate.PromptFile))
            {
                throw new InvalidOperationException($"Candidate '{candidate.Id}' promptFile cannot be empty.");
            }
            if (candidate.Prompt != null && candidate.PromptFile != null)
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidate.Id}' cannot set both prompt and promptFile.");
            }
        }
    }

    private static void ValidateTools(Aiursoft.AgentExam.Core.Models.ExamCandidate candidate)
    {
        if (candidate.Tools is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                $"Candidate '{candidate.Id}' must explicitly enable at least one tool.");
        }
        var registered = ToolRegistry.GetRegisteredToolNames().ToHashSet(StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in candidate.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool))
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidate.Id}' tools contains an empty tool name.");
            }
            if (!selected.Add(tool))
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidate.Id}' tools contains duplicate tool '{tool}'.");
            }
            if (!registered.Contains(tool))
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidate.Id}' tools contains unknown tool '{tool}'.");
            }
        }
    }

    private static string? ResolveCredential(
        string candidateId,
        CandidateAuthentication authentication)
    {
        var mode = authentication.Mode;
        if (mode is not ("none" or "apiKey" or "bearer"))
        {
            throw new InvalidOperationException(
                $"Candidate '{candidateId}' authentication mode must be none, apiKey or bearer.");
        }
        if (mode == "none")
        {
            if (authentication.EnvironmentVariable != null)
            {
                throw new InvalidOperationException(
                    $"Candidate '{candidateId}' cannot set an authentication environment variable in none mode.");
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(authentication.EnvironmentVariable))
        {
            throw new InvalidOperationException(
                $"Candidate '{candidateId}' authentication environmentVariable is required.");
        }
        var credential = Environment.GetEnvironmentVariable(authentication.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new InvalidOperationException(
                $"Candidate '{candidateId}' authentication environment variable is not set.");
        }
        return credential;
    }

    private static string ResolveRelativePath(
        string rootDirectory,
        string value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{field} is required.");
        }
        if (Path.IsPathRooted(value))
        {
            throw new InvalidOperationException($"{field} must be relative to the configuration file.");
        }
        try
        {
            return ExamValidation.ResolveContainedPath(rootDirectory, value);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{field} must remain inside the configuration directory.",
                exception);
        }
    }
}
