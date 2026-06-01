using Aiursoft.GptClient.Abstractions;
using Newtonsoft.Json;

namespace Aiursoft.Kanban.Services.Agent;

public class ToolMessagesItem : MessagesItem
{
    [JsonProperty("tool_calls")]
    public List<ToolCallData>? ToolCalls { get; set; }

    [JsonProperty("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonProperty("reasoning_content")]
    public string? ReasoningContent { get; set; }
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
