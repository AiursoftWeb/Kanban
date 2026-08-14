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
        : this(services, enabledToolNames: null)
    {
    }

    public ToolRegistry(IServiceProvider services, IReadOnlySet<string>? enabledToolNames)
    {
        var discoveredTools = DiscoverTools();
        ValidateEnabledToolNames(discoveredTools, enabledToolNames);

        foreach (var (name, type, method) in discoveredTools)
        {
            if (enabledToolNames != null && !enabledToolNames.Contains(name))
            {
                continue;
            }

            var metadata = new List<object>();
            var adviceAttr = method.GetCustomAttribute<AdviceAttribute>();
            if (adviceAttr != null)
            {
                metadata.Add(adviceAttr);
            }

            var tool = McpServerTool.Create(
                method: method,
                createTargetFunc: ctx =>
                    ctx.Services!.GetRequiredService(type),
                options: new McpServerToolCreateOptions
                {
                    Name = name,
                    Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                    Services = services,
                    Metadata = metadata
                });

            _allTools.Add(tool);
        }
    }

    public static IReadOnlyList<string> GetRegisteredToolNames() =>
        DiscoverTools().Select(tool => tool.Name).ToArray();

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

    private static IReadOnlyList<DiscoveredTool> DiscoverTools()
    {
        var discoveredTools = typeof(ToolRegistry).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => new
                {
                    Type = type,
                    Method = method,
                    Attribute = method.GetCustomAttribute<McpServerToolAttribute>()
                }))
            .Where(tool => tool.Attribute != null)
            .Select(tool => new DiscoveredTool(
                tool.Attribute!.Name ?? tool.Method.Name,
                tool.Type,
                tool.Method))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicateToolName = discoveredTools
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateToolName != null)
        {
            throw new InvalidOperationException($"Duplicate MCP tool name '{duplicateToolName}'.");
        }

        return discoveredTools;
    }

    private static void ValidateEnabledToolNames(
        IReadOnlyList<DiscoveredTool> discoveredTools,
        IReadOnlySet<string>? enabledToolNames)
    {
        if (enabledToolNames == null)
        {
            return;
        }

        var registeredToolNames = discoveredTools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var unknownToolNames = enabledToolNames
            .Where(toolName => !registeredToolNames.Contains(toolName))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownToolNames.Length > 0)
        {
            throw new ArgumentException(
                $"Unknown MCP tool name(s): {string.Join(", ", unknownToolNames)}.",
                nameof(enabledToolNames));
        }
    }

    private sealed record DiscoveredTool(string Name, Type Type, MethodInfo Method);
}
