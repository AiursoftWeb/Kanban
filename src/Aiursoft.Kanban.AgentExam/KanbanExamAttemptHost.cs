using Aiursoft.AgentExam.Core.Models;
using Aiursoft.Kanban.Services.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.Kanban.AgentExam;

public sealed record KanbanExamAttemptHostOptions
{
    public required IAgentModelClient ModelClient { get; init; }
    public required TimeProvider TimeProvider { get; init; }
    public required IReadOnlySet<string> EnabledToolNames { get; init; }
    public string? SystemPromptOverride { get; init; }
}

public sealed class KanbanExamAttemptHost : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly string? systemPromptOverride;

    private KanbanExamAttemptHost(
        WebApplication application,
        string? systemPromptOverride)
    {
        this.application = application;
        this.systemPromptOverride = systemPromptOverride;
    }

    public static KanbanExamAttemptHost Create(KanbanExamAttemptHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(Startup).Assembly.FullName,
            EnvironmentName = "AgentExam"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DbType"] = "InMemory",
            ["ConnectionStrings:DefaultConnection"] = $"kanban-exam-{Guid.NewGuid():N}",
            ["ConnectionStrings:AllowCache"] = "False",
            ["AppSettings:AuthProvider"] = "Local",
            ["AppSettings:Local:AllowRegister"] = "True",
            ["AuditLogs:Clickhouse:Enabled"] = "False",
            ["Logging:Clickhouse:Enabled"] = "False"
        });

        var startup = new Startup();
        startup.ConfigureServices(builder.Configuration, builder.Environment, builder.Services);
        builder.Services.AddKanbanExamAttempt(new KanbanExamAttemptOptions
        {
            ModelClient = options.ModelClient,
            TimeProvider = options.TimeProvider,
            EnabledToolNames = options.EnabledToolNames
        });
        var application = builder.Build();
        return new KanbanExamAttemptHost(application, options.SystemPromptOverride);
    }

    public async Task<AttemptEvidence> RunAsync(
        ExamScenario scenario,
        CancellationToken cancellationToken = default)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<KanbanExamAdapter>()
            .RunAsync(scenario, systemPromptOverride, cancellationToken);
    }

    public ValueTask DisposeAsync() => application.DisposeAsync();
}
