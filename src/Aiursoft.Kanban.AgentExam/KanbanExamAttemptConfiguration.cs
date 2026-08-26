using Aiursoft.Kanban.Services.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aiursoft.Kanban.AgentExam;

public sealed record KanbanExamAttemptOptions
{
    public required IAgentModelClient ModelClient { get; init; }
    public required TimeProvider TimeProvider { get; init; }
    public required IReadOnlySet<string> EnabledToolNames { get; init; }
}

public static class KanbanExamAttemptServiceCollectionExtensions
{
    public static IServiceCollection AddKanbanExamAttempt(
        this IServiceCollection services,
        KanbanExamAttemptOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ModelClient);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentNullException.ThrowIfNull(options.EnabledToolNames);
        if (options.EnabledToolNames.Count == 0)
        {
            throw new ArgumentException(
                "An exam attempt must explicitly enable at least one tool.",
                nameof(options));
        }

        var enabledToolNames = options.EnabledToolNames.ToHashSet(StringComparer.Ordinal);
        var knownToolNames = ToolRegistry.GetRegisteredToolNames().ToHashSet(StringComparer.Ordinal);
        var unknownToolNames = enabledToolNames
            .Where(name => !knownToolNames.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownToolNames.Length > 0)
        {
            throw new ArgumentException(
                $"Unknown MCP tool name(s): {string.Join(", ", unknownToolNames)}.",
                nameof(options));
        }

        services.RemoveAll<IAgentModelClient>();
        services.RemoveAll<TimeProvider>();
        services.RemoveAll<ToolRegistry>();
        services.RemoveAll<ProductionAgentExecutor>();
        services.RemoveAll<IAgentService>();
        services.AddSingleton(options.ModelClient);
        services.AddSingleton(options.TimeProvider);
        services.AddSingleton(sp => new ToolRegistry(sp, enabledToolNames));
        services.AddSingleton<ProductionAgentExecutor>();
        services.AddSingleton<IAgentService, AgentService>();
        services.AddScoped<KanbanExamScenarioSeeder>();
        services.AddScoped<KanbanExamStateSnapshotter>();
        services.AddScoped<KanbanExamAdapter>();
        return services;
    }
}
