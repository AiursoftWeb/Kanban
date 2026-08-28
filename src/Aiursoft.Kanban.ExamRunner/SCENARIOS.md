# Authoring Kanban exam scenarios

A scenario file contains one scenario object or an array of scenario objects. Scenario IDs must be unique across every matched file. The Runner loads files in stable ordinal path order and validates the entire set before executing a candidate.

The committed [`Scenarios/kanban-baseline-v1.json`](Scenarios/kanban-baseline-v1.json) is the reference fixture.

## Scenario structure

```json
{
  "schemaVersion": "1.0",
  "id": "read-urgent-cards",
  "name": "Find urgent cards",
  "description": "Use the focused priority query.",
  "domain": "kanban",
  "tags": ["read"],
  "weight": 1,
  "timeoutSeconds": 120,
  "fixedUtcNow": "2026-08-25T09:00:00Z",
  "setup": {
    "users": [],
    "boards": [],
    "columns": [],
    "shares": [],
    "cards": []
  },
  "steps": [
    {
      "userId": "user.owner",
      "boardId": "board.work",
      "userMessage": "Show urgent cards.",
      "expect": {
        "trace": [],
        "state": [],
        "response": []
      }
    }
  ]
}
```

Required top-level fields are `schemaVersion`, `id`, `name`, `domain`, `fixedUtcNow`, `setup`, and a non-empty `steps` array. The current schema and domain are `1.0` and `kanban`. `weight` and `timeoutSeconds` must be positive. `fixedUtcNow` must be a non-default UTC timestamp and is supplied to the isolated attempt through `TimeProvider`.

Every setup must explicitly contain the arrays `users`, `boards`, `columns`, `shares`, and `cards`, even when an array is empty. `labels`, `comments`, and `subscriptions` are optional arrays. Every step must provide `userId`, `boardId`, `userMessage`, and an `expect` object with `trace`, `state`, and `response` arrays.

Scenario and assertion IDs are lowercase slugs with single hyphen-separated segments. Setup aliases accept lowercase letters and digits separated by dots or hyphens, such as `card.release-notes`.

## Setup and aliases

Each scenario starts with an empty isolated exam database and declares all data it needs. There is no implicit default user, board, column, or card.

Declarations are validated in this dependency order:

1. `users`
2. `boards`
3. `columns`
4. `shares`
5. `cards`
6. `labels`
7. `comments`
8. `subscriptions`

A reference must point to an alias declared earlier. All aliases are unique across setup entity types.

```json
"setup": {
  "users": [
    { "id": "user.owner", "displayName": "Owner", "roles": [] },
    { "id": "user.reviewer", "displayName": "Reviewer", "roles": [] }
  ],
  "boards": [
    { "id": "board.review", "name": "Review", "ownerId": "user.owner", "isPublic": false }
  ],
  "columns": [
    { "id": "column.review.todo", "boardId": "board.review", "name": "To Do", "status": "NotStarted", "order": 0 }
  ],
  "shares": [
    { "boardId": "board.review", "userId": "user.reviewer", "permission": "Editable" }
  ],
  "cards": [
    {
      "id": "card.review",
      "columnId": "column.review.todo",
      "title": "Review release",
      "description": "Check behavior and tests",
      "creatorUserId": "user.owner",
      "assignedUserId": "user.reviewer",
      "priority": "High",
      "dueDate": "2026-08-28T09:00:00Z",
      "order": 0
    }
  ]
}
```

Allowed enum text is case-sensitive:

- column `status`: `NotStarted`, `InProgress`, `Completed`
- share `permission`: `ReadOnly`, `Editable`
- card `priority`: `Urgent`, `High`, `Medium`, `Low`, `None`

A share has exactly one of `userId` and `roleName`. Labels use `#RRGGBB` colors and can reference cards through `cardIds`.

`step.userId` and `step.boardId` must reference setup aliases. The adapter maps aliases to real database IDs before invoking production services. Aliases are not interpolated into `userMessage` or tool-parameter matchers: tool traces contain the runtime numeric board, column, and card IDs. Prefer state assertions for entity relationships that would otherwise require hard-coded runtime IDs.

## Sequential steps

All steps in one scenario run in order against the same isolated database. A write made in step 1 is visible to step 2. Each step still receives its own production conversation, tool trace, response, loop count, elapsed time, and post-step snapshot, and only its own `expect` arrays evaluate that evidence.

A new scenario, repetition, or candidate receives a new attempt host and database.

## Evidence state

After each step, the adapter snapshots the complete exam database. JSON property names are camelCase:

```json
{
  "users": [
    { "id": "user.owner", "displayName": "Owner", "roles": [] }
  ],
  "boards": [
    { "id": "board.work", "name": "Work", "ownerId": "user.owner", "isPublic": false, "isArchived": false, "order": 0 }
  ],
  "columns": [
    { "id": "column.work.todo", "boardId": "board.work", "name": "To Do", "status": "NotStarted", "order": 0 }
  ],
  "cards": [
    {
      "id": "card.task",
      "columnId": "column.work.todo",
      "title": "Task",
      "description": "",
      "creatorUserId": "user.owner",
      "assignedUserId": null,
      "priority": "None",
      "dueDate": null,
      "order": 0
    }
  ],
  "shares": [],
  "labels": [],
  "cardLabels": [],
  "comments": [],
  "subscriptions": []
}
```

Cards, labels, and comments created by tools receive stable attempt-local aliases beginning with `generated.`, such as `generated.card-7`. Tests should usually match their semantic fields rather than the generated numeric suffix.

A useful subset state assertion is:

```json
{
  "id": "urgent-card-created",
  "kind": "state",
  "dimension": "ParameterAccuracy",
  "points": 2,
  "required": true,
  "match": {
    "cards": {
      "$subset": [
        { "title": "Prepare release notes", "priority": "Urgent" }
      ]
    }
  }
}
```

## Assertion kinds

Assertions may be grouped under `trace`, `state`, or `response` for readability, but `kind` determines how they evaluate:

- `tool`: at least one tool trace matches `name`, optional `parameters` and `result`, and optional `minCount`/`maxCount`.
- `forbidTool`: succeeds when no matching tool trace exists. Use a negative `penalty`; `hardFail: true` makes a violation fail the scenario.
- `state`: matches the post-step database snapshot.
- `response`: matches the final assistant response string.
- `maxToolCalls`: `match` is the maximum number of calls in the step.
- `maxLoops`: `match` is the maximum production ReAct loop count in the step.

`tool` and `forbidTool` names are exact, case-sensitive production names from `ToolRegistry`. The Runner validates them before creating an attempt. Do not invent aliases or exam-only tools. A candidate's explicit `tools` whitelist must include every tool the candidate needs for the selected scenarios.

Every assertion ID is unique within a scenario, including across steps and expectation groups. An assertion needs positive `points` or a negative `penalty`. `required: true` makes a failed positive assertion fail the scenario. `hardFail` is most useful for prohibited safety behavior.

## Evaluation dimensions

`dimension` is one of:

- `IntentRecognition`
- `ToolSelection`
- `ParameterAccuracy`
- `Safety`
- `Efficiency`

Keep positive assertions for all five dimensions across a complete exam suite. The score calculator uses fixed weights of 30%, 25%, 20%, 15%, and 10%, respectively. A dimension with no positive denominator contributes zero.

Invalid attempts remain in the scenario denominator and earn zero. This prevents transport or setup failures from improving a candidate score by removing difficult cases.

## JSON matchers

Plain objects are recursive partial matches: specify only properties that matter. Plain arrays require the same length and order. Plain scalar values require the same JSON type and value.

A single-property operator object supports:

- `{ "$exact": value }`: exact recursive JSON equality.
- `{ "$contains": value }`: case-insensitive substring matching for strings, a matching member in an array, or partial object matching.
- `{ "$regex": "pattern" }`: regular expression against the actual value, with a one-second timeout.
- `{ "$oneOf": [a, b] }`: any listed candidate matches.
- `{ "$subset": [a, b] }`: every expected item matches some actual array item; order and additional actual items do not matter.
- `{ "$unorderedEquals": [a, b] }`: arrays have the same members and count, ignoring order.
- `{ "$exists": true }`: the actual value is not null/undefined; `false` expects null/undefined.
- `{ "$var": "name" }`: matches an evaluator variable. The built-in evaluator supplies no variables, so committed scenarios must not rely on this operator.

Tool parameter example that avoids runtime IDs:

```json
{
  "id": "find-urgent",
  "kind": "tool",
  "dimension": "ToolSelection",
  "points": 2,
  "required": true,
  "match": {
    "name": "GetCardsByPriority",
    "parameters": { "priority": "Urgent" },
    "minCount": 1,
    "maxCount": 1
  }
}
```

## Static validation

The CLI currently runs the exam and does not provide a validation-only mode. The committed fixtures are validated without any endpoint call by the focused test:

```sh
dotnet test tests/Aiursoft.Kanban.ExamRunner.Tests/Aiursoft.Kanban.ExamRunner.Tests.csproj \
  --filter "FullyQualifiedName~BaselineScenarioTests"
```

This loads the real files with `ScenarioLoader` and the production `ToolRegistry`, checks the expected scenario set and all five dimensions, and loads the example configuration with a temporary environment credential. Structural, reference, matcher, assertion-ID, and production-tool-name errors fail before any candidate model or web host is created.
