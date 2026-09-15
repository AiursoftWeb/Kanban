using System.Reflection;
using Aiursoft.AgentKit.Mcp;
using Aiursoft.Scanner.Abstractions;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Agent;

public class ToolRegistry : ISingletonDependency
{
    private readonly List<McpServerTool> _allTools = [];
    private readonly McpToolCatalog catalog;

    public IReadOnlyList<McpServerTool> AllTools => _allTools;
    internal McpToolCatalog Catalog => catalog;

    public ToolRegistry(IServiceProvider services)
        : this(services, enabledToolNames: null)
    {
    }

    public ToolRegistry(IServiceProvider services, IReadOnlySet<string>? enabledToolNames)
    {
        catalog = new McpToolCatalog(
            services,
            [typeof(ToolRegistry).Assembly],
            enabledToolNames,
            method => method.GetCustomAttribute<AdviceAttribute>() is not null,
            method => method.GetCustomAttribute<AdviceAttribute>() is { } advice ? [advice] : []);
        _allTools.AddRange(catalog.Tools.Select(registration => registration.Tool));
    }

    public static IReadOnlyList<string> GetRegisteredToolNames() =>
        McpToolCatalog.GetRegisteredToolNames([typeof(ToolRegistry).Assembly]);

    public bool IsWriteTool(string toolName) =>
        catalog.Get(toolName)?.RequiresApproval == true;

    public McpServerTool? GetTool(string toolName)
    {
        return _allTools.FirstOrDefault(t => t.ProtocolTool.Name == toolName);
    }

}
