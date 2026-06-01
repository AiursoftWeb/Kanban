using System.Text.Json.Serialization;

namespace Aiursoft.Kanban.Services.Agent;

public class ClaudeRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("messages")]
    public List<ClaudeMessage> Messages { get; set; } = [];

    [JsonPropertyName("tools")]
    public List<ClaudeTool>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class ClaudeMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    public static ClaudeMessage User(string text) => new() { Role = "user", Content = text };

    public static ClaudeMessage Assistant(List<ClaudeContentBlock> blocks, string? reasoningContent = null) => new()
    {
        Role = "assistant",
        Content = blocks,
        ReasoningContent = reasoningContent
    };

    public static ClaudeMessage ToolResult(string toolUseId, string result) => new()
    {
        Role = "user",
        Content = new List<ClaudeContentBlock>
        {
            new()
            {
                Type = "tool_result",
                ToolUseId = toolUseId,
                Content = result
            }
        }
    };
}

public class ClaudeContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Content { get; set; }

    public static ClaudeContentBlock TextBlock(string text) => new() { Type = "text", Text = text };

    public static ClaudeContentBlock ToolUse(string id, string name, Dictionary<string, object?> input) => new()
    {
        Type = "tool_use",
        Id = id,
        Name = name,
        Input = input
    };
}

public class ClaudeTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public object InputSchema { get; set; } = new { type = "object" };
}

public class ClaudeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public List<ClaudeContentBlock> Content { get; set; } = [];

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; set; }

    public string GetText() => string.Join("\n",
        Content.Where(c => c.Type == "text" && c.Text != null).Select(c => c.Text!));

    public List<ClaudeContentBlock> GetToolUses() =>
        Content.Where(c => c.Type == "tool_use").ToList();
}

public class ClaudeUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}
