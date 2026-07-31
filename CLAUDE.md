A self-hosted .NET Worker Service that turns Telegram messages into notes in
an Obsidian vault backed by a Git repository. Runs via docker compose on a
private Linux server. Target the current .NET LTS.

# MindBot — Claude Code Guidelines

Technical constraints and project conventions for Claude-assisted development.

## Fixed Technical Constraints

These are non-negotiable decisions. Do not substitute alternatives.

### Version Control
- Git access is via the `git` CLI invoked as a subprocess, **not** LibGit2Sharp
- The bot pushes to a dedicated branch (e.g. `bot-inbox`, configured via `GIT__BRANCH`)
- The operator merges this branch into main by hand
- **Invariant:** The bot is the only writer to this branch; must never merge, never force-push, never rewrite history
- **Invariant:** Never discard a commit without first writing **and verifying** a recovery
  bundle to `GIT__RECOVERYPATH`. If the bundle cannot be written, keep the commits and stay
  degraded — losing captures is never the lesser evil
- **Invariant:** When the operator has rewritten the branch after triage (the last pushed SHA is
  no longer an ancestor of origin), **do not rebase**. Rebasing replays commits whose notes were
  already processed, resurrecting deleted notes. Do not rely on rebase patch-id skipping to
  prevent this — it stops working once the operator edits a note during triage
- The steady state is **zero** un-pushed commits. The rebase and recovery paths are
  rarely-exercised code and need tests, not confidence

### Telegram Integration
- Access is via long polling with the **Telegram.Bot** NuGet package
- **Do not use:** webhooks or other transport methods

### Configuration & Data
- Frontmatter is serialized/deserialized with **YamlDotNet**
  - Never build or patch YAML by string manipulation
- Config binds from environment variables through `IOptions<T>`
- Validation happens at startup; failures must be fast and actionable
- **Invariant:** The bot's own state (SQLite database, recovery bundles) must live outside
  `VAULT__ROOT`. `git add -A` would otherwise commit it onto the branch. The options validators
  enforce this

### Durability
- Durable state is **SQLite via EF Core** (`Microsoft.EntityFrameworkCore.Sqlite`), with
  checked-in migrations applied at startup
- Accepting a Telegram update is **one transaction**: dedupe on `update_id`, route, reserve the
  filename, queue the write job, advance the offset. Never split these — the duplicate guard
  depends on their atomicity
- Keep git and filesystem work out of that transaction; it must not hold a SQLite write lock
  across a network round trip
- A write job stores its resolved filename and content, so replaying it after a crash rewrites
  the same path with the same bytes rather than allocating a second note

### Logging
- **Invariant:** Never log a raw Telegram URL. Telegram embeds the bot token in file-download
  URLs (`https://api.telegram.org/file/bot<token>/...`)
- Redaction belongs at the log formatter, not at call sites — the formatter is the single
  boundary every message passes through, including exceptions thrown inside Telegram.Bot

### Obsidian Vault Manipulation
- Obsidian is a desktop app and **is not running on this server**
- All vault operations are plain filesystem work on Markdown files
- **Do not attempt:** to invoke Obsidian, its plugins, or its REST API
- **Invariant:** No operation moves, renames, or deletes a note
  - This is by design, not an oversight — see project phase documentation

## Before Implementation

- **Verify current package and image versions before pinning**
  - Do not trust versions recalled from memory
  - Run package queries and check release dates
