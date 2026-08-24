using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.AgentExam.Core.Abstractions;
using Aiursoft.AgentExam.Core.Models;
using Aiursoft.AgentExam.Core.Validation;

namespace Aiursoft.AgentExam.Core.Reporting;

public sealed class ReportWriter : IReportWriter
{
    public async Task WriteAsync(
        ExamReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var jsonPath = ExamValidation.ResolveContainedPath(directory, "report.json");
        var htmlPath = ExamValidation.ResolveContainedPath(directory, "report.html");

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, JsonDefaults.Options),
            cancellationToken);
        await File.WriteAllTextAsync(
            htmlPath,
            BuildHtml(report),
            cancellationToken);
    }

    private static string BuildHtml(ExamReport report)
    {
        var html = new StringBuilder(
            "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<title>Agent Exam Report</title><style>" +
            "body{font-family:sans-serif;max-width:1100px;margin:2rem auto}" +
            "table{border-collapse:collapse;width:100%}" +
            "td,th{border:1px solid #ccc;padding:.5rem}" +
            ".pass{color:green}.fail{color:#b00}</style></head><body>" +
            "<h1>Agent Exam Report</h1>");
        html.Append($"<p>Schema: {Encode(report.SchemaVersion)} | Started: {Encode(report.StartedAt)} | Scenario hash: {Encode(report.ScenarioHash)}</p>");
        foreach (var candidate in report.Candidates)
        {
            html.Append($"<h2>{Encode(candidate.Id)} — {candidate.Score.Total:F1}/100</h2>");
            html.Append($"<p>Model: {Encode(candidate.Model)} | Strategy: {Encode(candidate.StrategyId)} | Repetition: {candidate.Repetition} | Prompt hash: {Encode(candidate.PromptHash)}</p>");
            html.Append("<table><tr><th>Dimension</th><th>Score</th><th>Weight</th><th>Contribution</th></tr>");
            foreach (var dimension in candidate.Score.Dimensions)
            {
                html.Append($"<tr><td>{Encode(dimension.Dimension)}</td><td>{dimension.Score:F1}</td><td>{dimension.Weight:P0}</td><td>{dimension.Contribution:F1}</td></tr>");
            }
            html.Append("</table><h3>Scenarios</h3><ul>");
            foreach (var scenario in candidate.Score.Scenarios)
            {
                AppendScenario(html, scenario);
            }
            html.Append("</ul>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void AppendScenario(StringBuilder html, ScenarioResult scenario)
    {
        var status = scenario.Passed ? "PASS" : scenario.Valid ? "FAIL" : "INVALID";
        html.Append($"<li class=\"{(scenario.Passed ? "pass" : "fail")}\">{Encode(scenario.ScenarioId)}: {status} — {scenario.Score:F1}/100");
        if (scenario.Error != null)
        {
            html.Append($" — {Encode(scenario.Error)}");
        }
        html.Append("<ul>");
        foreach (var assertion in scenario.Assertions)
        {
            html.Append($"<li>{Encode(assertion.Id)}: {(assertion.Matched ? "MATCH" : "MISS")}");
            if (!string.IsNullOrWhiteSpace(assertion.Detail))
            {
                html.Append($" — {Encode(assertion.Detail)}");
            }
            html.Append("</li>");
        }
        foreach (var step in scenario.Steps ?? [])
        {
            html.Append($"<li>Step {step.StepIndex}: user {Encode(step.UserId)}, board {Encode(step.BoardId)}, elapsed {Encode(step.Elapsed)}<ul>");
            foreach (var tool in step.Tools)
            {
                html.Append($"<li>{Encode(tool.Name)}: loop {tool.Loop}, elapsed {Encode(tool.Elapsed)}</li>");
            }
            html.Append("</ul></li>");
        }
        html.Append("</ul></li>");
    }

    private static string Encode(object value) =>
        WebUtility.HtmlEncode(value.ToString()) ?? string.Empty;
}
