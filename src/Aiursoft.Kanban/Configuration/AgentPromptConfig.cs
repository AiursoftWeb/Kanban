namespace Aiursoft.Kanban.Configuration;

/// <summary>
/// Prompt templates for the Kanban agent. Loaded from appsettings.json.
/// Supports placeholder: {userContext} — injected at runtime with current
/// user name, roles, owned boards, and the active board name/ID.
/// The prompt text itself lives in configuration; only the context block
/// is built in code.
/// </summary>
public class AgentPromptConfig
{
    /// <summary>
    /// System prompt sent to the LLM as the role definition.
    /// Supports placeholder: {userContext}
    /// </summary>
    public string SystemPrompt { get; init; } = string.Empty;
}
