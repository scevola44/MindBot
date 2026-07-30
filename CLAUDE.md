# MindBot — Claude Code Guidelines

Technical constraints and project conventions for Claude-assisted development.

## Fixed Technical Constraints

These are non-negotiable decisions. Do not substitute alternatives.

### Version Control
- Git access is via the `git` CLI invoked as a subprocess, **not** LibGit2Sharp
- The bot pushes to a dedicated branch (e.g. `bot-inbox`, configured via `GIT__BRANCH`)
- The operator merges this branch into main by hand
- **Invariant:** The bot is the only writer to this branch; must never merge, never force-push, never rewrite history

### Telegram Integration
- Access is via long polling with the **Telegram.Bot** NuGet package
- **Do not use:** webhooks or other transport methods

### Configuration & Data
- Frontmatter is serialized/deserialized with **YamlDotNet**
  - Never build or patch YAML by string manipulation
- Config binds from environment variables through `IOptions<T>`
- Validation happens at startup; failures must be fast and actionable

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
