# Kanban CLI

Install the .NET 10 tool from your configured NuGet source:

```bash
dotnet tool install --global Aiursoft.Kanban.Client
kanban --help
```

Create a personal API key from **Settings → Personal → API Keys** on your Kanban server.
Every operation runs with that user's current permissions and board access.
The CLI does not save the key. Supply `--server` and `--api-key` on each invocation.

```bash
kanban board list --server https://kanban.example.com --api-key "$KANBAN_API_KEY" --json
kanban board get --id 57 --server https://kanban.example.com --api-key "$KANBAN_API_KEY"
kanban column create --board-id 57 --name Review --server https://kanban.example.com --api-key "$KANBAN_API_KEY"
kanban card create --column-id 191 --title "Review CLI" --description "Acceptance task" --server https://kanban.example.com --api-key "$KANBAN_API_KEY" --json
kanban card update --id 802 --priority High --due-date 2026-11-01T12:00:00Z --server https://kanban.example.com --api-key "$KANBAN_API_KEY"
kanban card move --id 802 --column-id 196 --order 0 --server https://kanban.example.com --api-key "$KANBAN_API_KEY"
kanban card comment --id 802 --content "Ready for review" --server https://kanban.example.com --api-key "$KANBAN_API_KEY"
kanban search --query "Review CLI" --server https://kanban.example.com --api-key "$KANBAN_API_KEY" --json
kanban tasks --server https://kanban.example.com --api-key "$KANBAN_API_KEY" --json
```

`column update` accepts `--name` and `--status` (`NotStarted`, `InProgress`, `Completed`).
`column move --id … --order …` changes a column's position; `column delete --id …` deletes
the column and its cards. These operations enforce the server's existing permissions.
`card get --id …` includes descriptions and comments.

Card updates read the current card before sending the existing API's complete update request.
Omitted fields retain the values read, including assignment, dates and recurrence. Explicit
`--description ""` clears a description; `--assigned-user-id ""` unassigns the card. Dates
accept ISO 8601 timestamps and are sent in UTC. As with the existing API, simultaneous
edits are not version checked. Card moves stay within the current board.

Use a command's `--help` for its options. `--json` writes the complete AiurProtocol response
to standard output; errors go to standard error with a nonzero exit code.
