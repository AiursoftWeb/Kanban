using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.Agent;

public class ToolMessagesItem
{
    [JsonProperty("role")]
    public string? Role { get; set; }

    [JsonProperty("content")]
    public string? Content { get; set; }

    [JsonProperty("tool_calls")]
    public List<ToolCallData>? ToolCalls { get; set; }

    [JsonProperty("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonProperty("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonProperty("isMeta")]
    public bool IsMeta { get; set; }
}

public class ToolCallData
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; } = "function";

    [JsonProperty("function")]
    public ToolCallFunction? Function { get; set; }
}

public class ToolCallFunction
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("arguments")]
    public string? Arguments { get; set; }
}
