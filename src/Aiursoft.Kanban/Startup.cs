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
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Aiursoft.ClickhouseSdk.Abstractions;
using Aiursoft.Kanban.Services.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Kanban;

[ExcludeFromCodeCoverage]
public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<AnthropicConfiguration>(configuration.GetSection("AppSettings:Anthropic"));
        services.Configure<AgentPromptConfig>(configuration.GetSection("AppSettings:Agent"));
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
        services.AddSingleton<IAgentService, AgentService>();
        services.AddSingleton<ISubagent>(sp => sp.GetRequiredService<TaskPlanningSubagent>());

        // Background job infrastructure
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();

        // Background jobs
        services.RegisterBackgroundJob<DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<OrphanAvatarCleanupJob>();
        var auditLogFlushJob = services.RegisterBackgroundJob<AuditLogFlushService>();

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
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDefaultControllerRoute();
    }
}
