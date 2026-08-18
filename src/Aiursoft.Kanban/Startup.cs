using Aiursoft.CSTools.Tools;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Scanner;
using Aiursoft.Kanban.Configuration;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.Kanban.InMemory;
using Aiursoft.Kanban.MySql;
using Aiursoft.Kanban.Services.Authentication;
using Aiursoft.Kanban.Services.Agent;
using Aiursoft.Kanban.Services.Agent.Subagent;
using Aiursoft.Kanban.Services.BackgroundJobs;
using Aiursoft.Kanban.Sqlite;
using Aiursoft.UiStack;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Services.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Ganss.Xss;
using Markdig;

namespace Aiursoft.Kanban;

[ExcludeFromCodeCoverage]
public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<ClickhouseOptions>(configuration.GetSection("AuditLogs:Clickhouse"));


        // Relational database
        var (connectionString, dbType, allowCache) = configuration.GetDbSettings();
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: allowCache, splitQuery: false),
                new SqliteSupportedDb(allowCache: allowCache, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddHttpContextAccessor();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Startup).Assembly));
        services.AddSingleton<NavigationState<Startup>>();

        // Agent infrastructure
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAgentModelClient>(sp => sp.GetRequiredService<ClaudeClient>());
        services.AddSingleton<IAgentService, AgentService>();
        services.AddSingleton<ISubagent>(sp => sp.GetRequiredService<TaskPlanningSubagent>());

        // Background job infrastructure
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();

        // Background jobs
        services.RegisterBackgroundJob<DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<OrphanAvatarCleanupJob>();
        var auditLogFlushJob = services.RegisterBackgroundJob<AuditLogFlushService>();
        services.AddHostedService<AuditLogShutdownFlushService>();

        // Scheduled tasks (attach a schedule to any registered background job)
        services.RegisterScheduledTask(
            registration: orphanAvatarCleanupJob,
            period:     TimeSpan.FromHours(6),
            startDelay: TimeSpan.FromMinutes(5));
        services.RegisterScheduledTask(
            registration: auditLogFlushJob,
            period: TimeSpan.FromMinutes(2),
            startDelay: TimeSpan.FromSeconds(10));

        // Daily Report background job — scans every 30 minutes
        var dailyReportJob = services.RegisterBackgroundJob<DailyReportBackgroundJob>();
        services.RegisterScheduledTask(
            registration: dailyReportJob,
            period:      TimeSpan.FromMinutes(30),
            startDelay:  TimeSpan.FromMinutes(2));

        // Weekly Report background job — runs every hour, generates on Friday afternoon
        var weeklyReportJob = services.RegisterBackgroundJob<WeeklyReportBackgroundJob>();
        services.RegisterScheduledTask(
            registration: weeklyReportJob,
            period:      TimeSpan.FromHours(1),
            startDelay:  TimeSpan.FromMinutes(7));

        // Auto Set Planned Start Time background job — scans every 30 minutes
        var autoSetPlannedStartTimeJob = services.RegisterBackgroundJob<AutoSetPlannedStartTimeBackgroundJob>();
        services.RegisterScheduledTask(
            registration: autoSetPlannedStartTimeJob,
            period:      TimeSpan.FromMinutes(30),
            startDelay:  TimeSpan.FromMinutes(3));

        // Vector Embedding Background Jobs
        var generateEmbeddingsJob = services.RegisterBackgroundJob<GenerateCardEmbeddingsJob>();
        services.RegisterScheduledTask(
            registration: generateEmbeddingsJob,
            period: TimeSpan.FromMinutes(30),
            startDelay: TimeSpan.FromMinutes(50));

        var refreshEmbeddingCacheJob = services.RegisterBackgroundJob<RefreshCardEmbeddingCacheJob>();
        services.RegisterScheduledTask(
            registration: refreshEmbeddingCacheJob,
            period: TimeSpan.FromMinutes(60),
            startDelay: TimeSpan.FromMinutes(1));

        // Add the markdown pipeline and HTML sanitizer
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
        services.AddSingleton(pipeline);
        services.AddSingleton(_ =>
        {
            var sanitizer = new HtmlSanitizer();
            sanitizer.AllowedTags.Add("br");
            sanitizer.AllowedAttributes.Add("class");
            return sanitizer;
        });

        // Controllers and localization
        services.AddControllersWithViews(options => options.Filters.Add<AuditActionFilter>())
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Code500");
        app.UseStatusCodePagesWithReExecute("/Error/Code{0}");
        app.UseUIStack();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
    }
}
