# Kanban Agent Exam Runner

The Runner evaluates a candidate model through the real Kanban production agent stack. It reuses the production system prompt, tool schemas, ReAct loop, access checks, current-user context, and tool implementations. It does not start the Kanban web server or connect to a production Kanban database.

Each candidate, repetition, and scenario runs in a fresh application service provider and EF Core InMemory database. Steps within one scenario share that scenario's database, allowing a later step to inspect an earlier write. The attempt host automatically approves production write-tool advice only inside this isolated exam environment.

Only the `production` strategy is registered. An unknown strategy fails explicitly; there is no scripted fallback that can produce a quality score without running the production agent.

## Quick start

Copy the example configuration to the ignored local filename, set its credential environment variable, and run from the repository root:

```sh
cp src/Aiursoft.Kanban.ExamRunner/exam-config.example.json \
  src/Aiursoft.Kanban.ExamRunner/exam-config.json
export ANTHROPIC_API_KEY='...'
dotnet run --project src/Aiursoft.Kanban.ExamRunner -- \
  --config src/Aiursoft.Kanban.ExamRunner/exam-config.json
```

The CLI accepts exactly `--config <path>`. It has no `--scenarios`, configuration override, or `--validate-only` option. The focused `BaselineScenarioTests` test validates the committed baseline and example configuration without calling a model endpoint.

## Candidate endpoint

`endpoint` must be a complete absolute HTTP or HTTPS URL for an Anthropic Messages-compatible service, for example `https://gateway.example.test/v1/messages`. The Runner sends a `POST` to the URL exactly as configured and does not append a path. Configure any provider-required compatibility headers at the gateway; the Runner itself only adds the selected authentication header.

Requests preserve the production agent's system prompt, message order, tool definitions, model, and maximum token setting. The body uses the Messages-compatible fields `model`, `max_tokens`, `system`, `messages`, `tools`, and `stream: false`. Responses must contain compatible text and `tool_use` content blocks.

Authentication is explicit and mutually exclusive:

```json
"authentication": {
  "mode": "apiKey",
  "environmentVariable": "ANTHROPIC_API_KEY"
}
```

- `none`: sends neither `x-api-key` nor `Authorization`; `environmentVariable` must be omitted.
- `apiKey`: reads the named environment variable and sends only `x-api-key`.
- `bearer`: reads the named environment variable and sends only `Authorization: Bearer ...`.

The environment variable must exist when the configuration is loaded. Do not put a credential value in the JSON file. Credentials and failed endpoint response bodies are not written to reports.

## Configuration

See [`exam-config.example.json`](exam-config.example.json) for a complete configuration:

```json
{
  "schemaVersion": "1.0",
  "scenarios": ["Scenarios/**/*.json"],
  "outputDirectory": "reports",
  "failBelow": 70,
  "candidates": [
    {
      "id": "claude-opus",
      "endpoint": "https://gateway.example.test/v1/messages",
      "model": "claude-opus-5",
      "strategyId": "production",
      "tools": ["SearchCards", "CreateCard"],
      "authentication": {
        "mode": "apiKey",
        "environmentVariable": "ANTHROPIC_API_KEY"
      },
      "repetitions": 1
    }
  ]
}
```

Top-level fields:

- `schemaVersion`: currently `1.0`.
- `scenarios`: one or more files, directories, or glob patterns. `*`, `?`, and `**` are supported. Matches are normalized, deduplicated, and loaded in ordinal path order.
- `outputDirectory`: report root. The default is `reports`.
- `failBelow`: mean candidate score threshold from 0 through 100.
- `candidates`: one or more candidates with unique IDs.

Scenario patterns and the output directory are resolved relative to the configuration file. Absolute paths and paths that escape its directory are rejected.

Candidate fields:

- `id`: required lowercase slug consisting of letters, digits, and single hyphen-separated segments.
- `endpoint` and `model`: required non-empty values; the endpoint must be absolute HTTP or HTTPS.
- `strategyId`: defaults to `production`, the only registered strategy.
- `tools`: required, non-empty, duplicate-free, case-sensitive whitelist of names from the production `ToolRegistry`. The whitelist controls both schemas sent to the candidate and tools that can execute.
- `authentication`: explicit authentication mode described above.
- `repetitions`: complete independent exam runs; defaults to `1` and must be positive.
- `prompt` / `promptFile`: optional and mutually exclusive system-prompt overrides. `promptFile` is resolved relative to the configuration file. If both are omitted, the production default prompt is used.

The prompt body is not serialized into a report; reports contain its SHA-256 hash.

## Reports and exit codes

Each invocation reserves a UTC timestamped directory and writes:

```text
<output>/<yyyy-MM-dd-HHmmss[-NN]>/<candidate-id>/repetition-N/report.json
<output>/<yyyy-MM-dd-HHmmss[-NN]>/<candidate-id>/repetition-N/report.html
<output>/<yyyy-MM-dd-HHmmss[-NN]>/summary.json
<output>/<yyyy-MM-dd-HHmmss[-NN]>/summary.html
```

The timestamp uses UTC. If another current or historical run already owns the same second, the Runner adds `-01`, `-02`, and so on rather than overwriting it. `outputDirectory` remains the configuration-relative report root, while the CLI prints the concrete directory reserved for the current invocation.

A scenario infrastructure, model, setup, timeout, or adapter failure becomes an invalid zero-score scenario; later scenarios, repetitions, and candidates continue, and reports are still produced.

Exit codes:

- `0`: every run is complete and every candidate mean meets `failBelow`.
- `1`: invalid CLI usage, configuration/startup failure, cancellation, or another fatal error before normal completion.
- `2`: reports were produced, but at least one run is incomplete or a candidate mean is below `failBelow`.

> **Treat report files as sensitive.** JSON reports contain candidate responses, tool parameters and results, assertion evidence, and full database snapshots. These may include user, board, card, comment, and other business data. Keep reports access-controlled and do not commit or publish them. The default `reports/` directory and local `exam-config.json` are ignored by Git.

## Scenarios

The committed baseline is [`Scenarios/kanban-baseline-v1.json`](Scenarios/kanban-baseline-v1.json). It covers read intent, a multi-step card lifecycle, private-board authorization, ambiguous destructive requests, and all five evaluation dimensions.

See [`SCENARIOS.md`](SCENARIOS.md) for the exact schema, setup aliases, evidence snapshot, assertion kinds, and matcher behavior.
