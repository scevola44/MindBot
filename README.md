# MindBot

A self-hosted .NET Worker Service that turns Telegram messages into notes in
an Obsidian vault backed by a Git repository. Runs via docker compose on a
private Linux server. Targets the current .NET LTS (.NET 10).

Send a plain text message to the bot; a Markdown note appears in the vault
repository on a dedicated branch and is pushed to the remote. The operator
merges that branch into `main` by hand.

## How it works

- **Telegram**: long polling only (via the `Telegram.Bot` package), never webhooks.
- **Authorisation**: only sender IDs listed in `TELEGRAM__ALLOWEDUSERIDS` may use
  the bot. Everyone else gets a flat refusal, and the attempt is logged.
- **Notes**: each message becomes `{yyyyMMddHHmm}.md` in the vault's
  `05 - Fleeting` folder, with YAML frontmatter (`date`, `tags: [WIP, MindBot]`)
  and the message body verbatim. `/new` files a named note (`groceries.md`)
  instead. Filenames are minute-precision, so a second note in the same minute
  gets a `-2` suffix (`202607311430-2.md`) rather than overwriting the first.
- **Tasks**: `/task` (or `/todo`) appends one checklist item per line to that
  day's `TODO - {yyyy-MM-dd}.md` in `06 - Daily Notes/{yyyy}/{MM - Month}`,
  creating it on first use and updating its `last-modified` property (while
  preserving `date`) on every later use the same day.
- **YouTube summaries**: `/ytsummary <url> [chunks]` files an AI-generated
  summary of a video as a note in `05 - Fleeting`, named after the video's title.
  See [YouTube summaries](#youtube-summaries) below.
- **Git**: the bot drives the `git` CLI directly (no LibGit2Sharp). It only ever
  reads from and writes to one dedicated branch (`GIT__BRANCH`, e.g. `bot-inbox`)
  and never merges, force-pushes, or rewrites history on it.
- **Vault**: Obsidian itself is never invoked — this is plain filesystem work
  on Markdown files. No operation ever moves, renames, or deletes a note.
- **Durability**: accepting a message and queueing the resulting note happen in
  one SQLite transaction, so a crash can neither lose a capture nor duplicate
  one. See [Durability](#durability) below.

## Project layout

```
src/
  MindBot.Core/           Options, note/filename/frontmatter logic, the message
                          router, the vault sync orchestration, and the
                          IGitService / durability abstractions — no filesystem
                          or process I/O.
  MindBot.Infrastructure/ GitService (git CLI subprocess), the EF Core SQLite
                          state store, the n8n HTTP client, and the vault file
                          writer — the only project that touches disk, spawns
                          processes, or opens sockets.
  MindBot.Bot/            Host: DI wiring, config validation, the git startup
                          self-check, the Telegram ingest loop, the drain
                          worker, the background summary worker, and the health
                          endpoint.
tests/
  MindBot.Tests/          xUnit tests, including git suites that run against a
                          real local bare repository and durability tests that
                          run against a real SQLite file.
```

Core/Infrastructure/Bot are separate projects (not just folders) so the
dependency direction is enforced by the compiler — Core has no knowledge of
git or the filesystem — and so the test project can exercise Core and
Infrastructure in isolation from the Generic Host bootstrapping in Bot.

## Configuration

All configuration is bound from environment variables via `IOptions<T>` and is
validated at startup — a missing or invalid setting fails the container fast,
with a message naming the exact variable, instead of failing silently later.

| Variable | Required | Description |
| --- | --- | --- |
| `TELEGRAM__BOTTOKEN` | yes | Bot token from [@BotFather](https://t.me/BotFather). |
| `TELEGRAM__ALLOWEDUSERIDS` | yes | Comma-separated Telegram numeric user IDs allowed to use the bot. |
| `GIT__REMOTEURL` | yes | SSH remote URL of the vault repository. |
| `GIT__BRANCH` | yes | The branch the bot exclusively reads from and writes to (e.g. `bot-inbox`). |
| `GIT__SSHKEYPATH` | yes | Path to the mounted SSH private key. Must exist and not be group/world readable (`chmod 600`). |
| `GIT__KNOWNHOSTSPATH` | no | Path to a `known_hosts` file. Falls back to ssh's default if unset. |
| `GIT__USERNAME` | no | Local `user.name` for commits (default `MindBot`). |
| `GIT__USEREMAIL` | no | Local `user.email` for commits (default `mindbot@localhost`). |
| `GIT__RECOVERYPATH` | no | Where recovery bundles are written (default `/data/recovery`). **Must be outside `VAULT__ROOT`** — startup fails otherwise. |
| `GIT__BATCHWINDOWSECONDS` | no | How long to coalesce arriving notes into one commit (default `5`). |
| `GIT__MAXBATCHSIZE` | no | Maximum notes per commit (default `100`). |
| `GIT__PUSHRETRYCOUNT` | no | Push attempts before reporting a degraded state (default `3`). |
| `GIT__PUSHRETRYBASESECONDS` | no | Base delay for the exponential push backoff (default `2`). |
| `VAULT__ROOT` | yes | Absolute path to the local clone of the vault (typically a mounted named volume). |
| `STATE__DATABASEPATH` | no | SQLite durability database (default `/data/mindbot.db`). **Must be outside `VAULT__ROOT`** — startup fails otherwise. |
| `STATE__CONVERSATIONEXPIRYMINUTES` | no | How long a half-finished `/new` conversation survives (default `60`). |
| `STATE__PROCESSEDUPDATERETENTIONDAYS` | no | How long processed update IDs are kept (default `7`). |
| `TELEGRAM__OPERATORCHATID` | no | Chat that receives operational alerts. When unset, alerts are logged instead. |
| `N8N__BASEURL` | no | Base URL of the n8n webhooks backing `/ytsummary` (e.g. `https://n8n.internal/webhook`). When unset the command is rejected with an explanation and the bot starts normally. |
| `N8N__TIMEOUTSECONDS` | no | Per-request timeout for those webhooks (default `600` — the summarisation step is LLM-bound). |
| `N8N__MAXATTEMPTS` | no | How many times a summary job re-runs the whole pipeline before giving up (default `3`). |
| `N8N__RETRYBASESECONDS` | no | Base delay for the exponential backoff between those attempts (default `30`). |
| `TZ` | no | Container timezone, used for the `date` frontmatter timestamp. |

## Running locally

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

To run the bot directly (outside Docker), export the environment variables
above, then:

```bash
dotnet run --project src/MindBot.Bot
```

## Running with Docker

A pre-built image is published to the GitHub Container Registry on every push
to `main` (tagged `latest`) and on every `vX.Y.Z` release tag (tagged with
matching semver tags), so deploying no longer requires cloning the repository
or building the image yourself:

```
ghcr.io/scevola44/mindbot:latest
```

```bash
mkdir -p secrets
cp /path/to/your/deploy-key secrets/id_ed25519
chmod 600 secrets/id_ed25519
ssh-keyscan your-git-host.example.com > secrets/known_hosts

cat > .env <<'EOF'
TELEGRAM__BOTTOKEN=123456789:your-bot-token
TELEGRAM__ALLOWEDUSERIDS=111111111,222222222
GIT__REMOTEURL=git@your-git-host.example.com:you/vault.git
GIT__BRANCH=bot-inbox
TZ=Europe/London
EOF

docker compose up -d
```

`docker-compose.yml` pulls `ghcr.io/scevola44/mindbot:${MINDBOT_TAG:-latest}`
by default — set `MINDBOT_TAG` in `.env` to pin a specific version. Only the
`docker-compose.yml` and `.env` files are needed on the server; the rest of
the repository is not required for deployment.

If you're developing MindBot itself and want to build from source instead of
pulling the published image, clone the repository and run:

```bash
docker compose up -d --build
```

GHCR images published from a private repository are private by default. Either
make the package public in the repository's Packages settings, or authenticate
the server once with a [personal access token](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry#authenticating-to-the-container-registry) that has `read:packages` scope:

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u your-github-username --password-stdin
```

`docker-compose.yml` mounts:

- a named volume (`vault-data`) at `/vault` for the persistent clone,
- a named volume (`state-data`) at `/data` for the SQLite durability database
  and recovery bundles — this must stay outside `/vault`, or `git add -A` would
  commit the bot's own state onto the branch,
- the SSH private key read-only at `/run/secrets/git_ssh_key`,
- a `known_hosts` file read-only at `/run/secrets/known_hosts`.

On start the bot migrates its state database, clones the repository if needed,
checks out `GIT__BRANCH` (creating it from the default branch and pushing it if
it doesn't exist on the remote), drains any write jobs left over from the
previous run, and only then starts polling Telegram — so a misconfigured deploy
(bad token, unreachable remote, non-writable branch) fails the container visibly
instead of silently dropping messages, and a restart never begins accepting new
messages on top of an unprocessed backlog.

## Durability

Accepting a message and writing its note are separate stages, connected by a
SQLite queue:

```
Telegram ──► ingest (one SQLite transaction per update)
              dedupe on update_id → route → reserve filename → queue the note
                                                   │
                                                   ▼
             drain worker ──► classify → pull → write batch → ONE commit → push
```

The ingest transaction is what makes the guarantees hold. A crash before it
commits leaves no trace and Telegram redelivers the update; a crash after it
commits leaves the update recorded, so the redelivery is skipped. There is no
window in which a note is written twice or dropped — and because nothing is
buffered in memory, `SIGTERM` only has to finish the batch already in flight.

### The core invariant

The bot pushes immediately after every commit and normally holds **zero**
un-pushed commits. While that holds, every pull is a fast-forward and conflicts
cannot occur. `unpushedCommitCount` on the health endpoint is the check: any
non-zero value is a degraded state, and the recovery paths below are live.

### Pre-write classification

Before every pull the bot asserts a clean working tree (committing anything an
unclean shutdown left behind), fetches, and then classifies:

| Case | Condition | Action |
| --- | --- | --- |
| 1 | No local commits unreachable from `origin/<branch>` | `pull --ff-only`. The normal path. |
| 2 | Un-pushed commits exist, and `lastPushedSha` is still an ancestor of origin | The operator advanced the branch. `pull --rebase --autostash`. |
| 3 | Un-pushed commits exist, and `lastPushedSha` is **not** an ancestor of origin | The branch was rewritten after triage. **Do not rebase** — export to a bundle, then `reset --hard origin/<branch>`. |
| 4 | Fetch failed | Log a warning and write locally anyway. A capture is never dropped because the remote is down. |

Case 3 is the one that matters. Rebasing there would replay commits whose notes
the operator has already processed, resurrecting deleted notes. Rebase's
patch-id skipping is *not* relied on to prevent this: it stops working the
moment the operator edits a note during triage.

The bot never force-pushes, never merges, and never discards a commit without
first writing and verifying a recovery bundle. If the bundle cannot be written,
it keeps the commits and stays degraded rather than dropping them.

### Recovering from a rewritten branch

When case 3 fires, the operator is messaged with the bundle path and the commit
count. To inspect what was set aside:

```bash
cd /path/to/vault-clone
git fetch /data/recovery/bot-inbox-20260731T143000Z.bundle HEAD:recovered-notes
git log recovered-notes
```

Cherry-pick anything worth keeping. The bundle is the only copy of those
commits, so collect it before pruning the `state-data` volume.

## YouTube summaries

`/ytsummary <youtube-url> [chunks]` produces an AI-generated summary of a video
as a note. It is the only command that does not answer immediately: the work
runs through five n8n webhooks — transcript, chunking, per-chunk summarisation,
reduction, keyword extraction — and takes minutes.

```
/ytsummary https://youtu.be/qIeJ7Gw9v_I
/ytsummary https://youtu.be/qIeJ7Gw9v_I 4
```

The optional second argument is how many pieces the transcript is split into
(1–12). Left out, it is derived from the transcript's length at roughly 1800
words per chunk — more chunks means more, smaller LLM calls and a more detailed
summary.

The resulting note lands in `05 - Fleeting`, named after the video's title
(slugified), with `tags: [WIP, Youtube, AISummary]`, the extracted keywords as
`[[wikilinks]]`, a title heading, a source link, and a `table-of-contents` block
for Obsidian's Table of Contents plugin.

`N8N__BASEURL` must point at the n8n instance hosting these five webhook paths:
`get-yt-transcript`, `text-chunker`, `summarize-chunks`, `chunks-reducer`,
`extract-keywords`. When it is unset the command is rejected with an explanation
and nothing else changes. The video's title comes from YouTube's public oEmbed
endpoint, which needs no API key; if that lookup fails the note falls back to the
video id.

Requests are durable in the same sense as notes. `/ytsummary` records a
background job in the same SQLite transaction that marks the Telegram update
processed, so a restart mid-pipeline resumes the job instead of losing it, and
the note is queued in a single transaction with the job's completion — it can
never be filed twice. A failed attempt is retried with an exponential backoff
(`N8N__MAXATTEMPTS`, `N8N__RETRYBASESECONDS`); when the attempts run out, the
chat is told why.

`/preview /ytsummary <url>` reports what would be queued without contacting n8n.

## Health endpoint

The bot serves `GET /health` on port 8080 inside the container. The port is
deliberately not published — the compose healthcheck probes it from inside.

```json
{
  "status": "healthy",
  "degraded": false,
  "lastSuccessfulPollUtc": "2026-07-31T14:29:58+00:00",
  "lastSuccessfulPushUtc": "2026-07-31T14:28:11+00:00",
  "queueDepth": 0,
  "unpushedCommitCount": 0,
  "workingTreeDirty": false,
  "lastClassification": "FastForward"
}
```

A degraded git state reports `degraded: true` but still returns **200**: a
remote that is down for an hour is a condition this bot is designed to ride out,
not a broken container. Only a stalled poller (no successful poll for 180s) or
an unreachable state database returns **503**.

```bash
docker compose exec mindbot curl -fsS http://localhost:8080/health
docker compose ps   # STATUS should read (healthy)
```

## Logging

Logs are one JSON object per line, with the sender ID attached as a scope.

The bot token is redacted at the log formatter rather than at call sites,
because Telegram embeds the token in file-download URLs
(`https://api.telegram.org/file/bot<token>/...`) — so an exception message can
leak it even though nothing logs the setting itself. Redacting at the single
boundary every message passes through covers those too.

## Out of scope

Media handling remains out of scope. No operation moves, renames, or deletes a
note — including on collision, which is why filenames gain a `-2` suffix rather
than overwriting.
