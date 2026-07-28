using Aiursoft.Scanner.Abstractions;
using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.Html;

namespace Aiursoft.Kanban.Services;

public class KanbanMarkdownService(MarkdownPipeline pipeline, HtmlSanitizer sanitizer) : ITransientDependency
{
    public string ConvertMarkdownToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = Markdown.ToHtml(markdown, pipeline);
        html = sanitizer.Sanitize(html);
        return html;
    }

    public HtmlString RenderMarkdown(string? markdown)
    {
        return new HtmlString(ConvertMarkdownToHtml(markdown));
    }
}
