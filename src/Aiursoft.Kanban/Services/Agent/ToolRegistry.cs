using System.ComponentModel;
using System.Reflection;
using Aiursoft.Scanner.Abstractions;
using ModelContextProtocol.Server;

namespace Aiursoft.Kanban.Services.Agent;

public class ToolRegistry : ISingletonDependency
{
    private readonly List<McpServerTool> _allTools = [];

    public IReadOnlyList<McpServerTool> AllTools => _allTools;

    public ToolRegistry(IServiceProvider services)
    {
        var toolTypes = typeof(ToolRegistry).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr == null) continue;

                var metadata = new List<object>();
                var adviceAttr = method.GetCustomAttribute<AdviceAttribute>();
                if (adviceAttr != null)
                    metadata.Add(adviceAttr);

                var tool = McpServerTool.Create(
                    method: method,
                    createTargetFunc: ctx =>
                        ctx.Services!.GetRequiredService(type),
                    options: new McpServerToolCreateOptions
                    {
                        Name = toolAttr.Name ?? method.Name,
                        Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                        Services = services,
                        Metadata = metadata
                    });

                _allTools.Add(tool);
            }
        }
    }

    public bool IsWriteTool(string toolName)
    {
        var tool = _allTools.FirstOrDefault(t => t.ProtocolTool.Name == toolName);
        if (tool == null) return false;
        return tool.Metadata.Any(m => m is AdviceAttribute);
    }

    public McpServerTool? GetTool(string toolName)
    {
        return _allTools.FirstOrDefault(t => t.ProtocolTool.Name == toolName);
    }
}
