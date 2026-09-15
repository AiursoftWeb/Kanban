using System.Text.Json;
using Aiursoft.AgentKit;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.Kanban.Services.Agent;

/// <summary>Provider adapter that preserves the existing Kanban Messages transport.</summary>
internal sealed class KanbanAgentKitModelClient(IAgentModelClient inner) : Aiursoft.AgentKit.IAgentModelClient
{
    public async ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
    {
        var systemPrompt = string.Concat(request.Transcript
            .Where(message => message.Role == TranscriptRole.System)
            .Select(GetText));
        var messages = new List<ClaudeMessage>();
        foreach (var message in request.Transcript.Where(message => message.Role != TranscriptRole.System))
        {
            switch (message.Role)
            {
                case TranscriptRole.User:
                    messages.Add(ClaudeMessage.User(GetText(message)));
                    break;
                case TranscriptRole.Assistant:
                    var blocks = message.Content.OfType<TextBlock>().Select(block => ClaudeContentBlock.TextBlock(block.Text)).ToList();
                    blocks.AddRange(message.ToolCalls.Select(call => ClaudeContentBlock.ToolUse(
                        call.Id,
                        call.Name,
                        JsonSerializer.Deserialize<Dictionary<string, object?>>(call.Arguments.GetRawText()) ?? new())));
                    messages.Add(ClaudeMessage.Assistant(blocks));
                    break;
                case TranscriptRole.Tool:
                    messages.AddRange(message.ToolResults.Select(result => ClaudeMessage.ToolResult(
                        result.CallId,
                        result.Output?.GetRawText() ?? result.Error ?? string.Empty)));
                    break;
                default:
                    throw new InvalidOperationException("Unknown AgentKit transcript role.");
            }
        }

        var tools = request.Tools.Select(tool => new ClaudeTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = JsonSerializer.Deserialize<object>(tool.InputSchema.GetRawText())!
        }).ToList();
        var response = await inner.SendAsync(systemPrompt, messages, tools, cancellationToken);
        var content = response.Content
            .Where(block => block.Type == "text" && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => (AgentContentBlock)new TextBlock(block.Text!))
            .ToList();
        content.AddRange(response.GetToolUses().Select(toolUse =>
        {
            if (string.IsNullOrWhiteSpace(toolUse.Id) || string.IsNullOrWhiteSpace(toolUse.Name))
                throw new InvalidOperationException("Provider returned an invalid tool call.");
            return (AgentContentBlock)new ToolCallBlock(new ToolCall(
                toolUse.Id,
                toolUse.Name,
                JsonSerializer.SerializeToElement(toolUse.Input ?? new Dictionary<string, object?>())));
        }));
        return new AgentModelResponse(content, response.StopReason switch
        {
            "end_turn" => AgentFinishReason.Stop,
            "tool_use" => AgentFinishReason.ToolCalls,
            "max_tokens" => AgentFinishReason.Length,
            "refusal" => AgentFinishReason.Refusal,
            _ => throw new InvalidOperationException($"Unsupported provider stop reason '{response.StopReason}'.")
        });
    }

    private static string GetText(TranscriptMessage message) =>
        string.Concat(message.Content.OfType<TextBlock>().Select(block => block.Text));
}
