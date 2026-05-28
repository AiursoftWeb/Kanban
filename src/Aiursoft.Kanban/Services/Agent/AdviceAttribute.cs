namespace Aiursoft.Kanban.Services.Agent;

/// <summary>
/// Marks a tool method so the agent intercepts execution and creates an Advice record
/// for user approval before running. Tools without this attribute execute immediately.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AdviceAttribute : Attribute
{
}
