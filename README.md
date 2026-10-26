# MindBot

A self-hosted .NET Worker Service that turns chat messages into notes in an
Obsidian vault backed by a Git repository. Messages arrive over Telegram, over
MindBot's own web chat API, or both. Runs via docker compose on a private Linux
server. Targets the current .NET LTS (.NET 10).

Send a plain text message to the bot; a Markdown note appears in the vault
repository on a dedicated branch and is pushed to the remote. The operator
merges that branch into `main` by hand.

## How it works

- **Channels**: Telegram and the web chat API, each optional, at least one
  required. Both feed the same commands, notes and durability guarantees; a
  reply always goes back on the channel the message came in on.
- **Telegram**: long polling only (via the `Telegram.Bot` package), never webhooks.
  Telegram can read bot chats, so the web channel exists for messages that
  should not pass through it. See [Web chat channel](#web-chat-channel).
- **Authorisation**: on Telegram, only sender IDs listed in
  `TELEGRAM__ALLOWEDUSERIDS` may use the bot; everyone else gets a flat refusal,
  and the attempt is logged. On the web channel, every request needs a
  per-device token.
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
                          IGitService / durability / web-transcript
                          abstractions — no filesystem or process I/O.
  MindBot.Infrastructure/ GitService (git CLI subprocess), the EF Core SQLite
                          state store, the n8n HTTP client, and the vault file
                          writer — the only project that touches disk, spawns
                          processes, or opens sockets.
  MindBot.Bot/            Host: DI wiring, config validation, the git startup
                          self-check, the Telegram ingest loop, the web chat
                          API and WebSocket (Web/), the drain worker, the
                          background summary worker, the health endpoint, and
                          the `device` CLI.
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
Enabling neither channel (no `TELEGRAM__BOTTOKEN` and `WEB__ENABLED` not `true`)
is such a failure.

| Variable | Required | Description |
| --- | --- | --- |
| `TELEGRAM__BOTTOKEN` | no | Bot token from [@BotFather](https://t.me/BotFather). Unset switches the Telegram channel off. |
| `TELEGRAM__ALLOWEDUSERIDS` | with a token | Comma-separated Telegram numeric user IDs allowed to use the bot. |
| `WEB__ENABLED` | no | `true` turns on the web chat channel (default `false`). |
| `WEB__PORT` | no | In-container port for the web API and client (default `8081`). Must not be `8080`. Reach it over a VPN only — see [Web chat channel](#web-chat-channel). |
| `WEB__STATICROOT` | no | Absolute path to the exported web client to serve on `WEB__PORT`. The API works without it. |
| `WEB__HISTORYRETENTIONDAYS` | no | How long the web chat history is kept (default `30`). Replies can quote vault content. |
| `WEB__MAXMESSAGELENGTH` | no | Longest accepted web message, in characters (default `16000`). |
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
| `TELEGRAM__OPERATORCHATID` | no | Telegram chat that receives operational alerts. When unset, alerts go into the web chat if it is enabled, otherwise to the log. |
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
previous run, and only then starts polling Telegram (when enabled) — so a misconfigured deploy
(bad token, unreachable remote, non-writable branch) fails the container visibly
instead of silently dropping messages, and a restart never begins accepting new
messages on top of an unprocessed backlog.

## Durability

Accepting a message and writing its note are separate stages, connected by a
SQLite queue:

```
Telegram ──┐
           ├─► ingest (one SQLite transaction per message)
Web API  ──┘    dedupe on (channel, message id) → route → reserve filename → queue the note
                                                   │
                                                   ▼
             drain worker ──► classify → pull → write batch → ONE commit → push
```

The ingest transaction is what makes the guarantees hold. A crash before it
commits leaves no trace and the message is delivered again (Telegram redelivers
the update; a web client retries with the same `clientMessageId`); a crash after
it commits leaves the message recorded, so the redelivery is skipped. Message
ids are only unique within their channel, so every record carries the channel. There is no
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

## Web chat channel

Telegram bot chats are not end-to-end encrypted, so Telegram can read them —
including vault content that replies like `/preview` quote back. The web channel
is MindBot's own chat API, for a self-hosted client, so those messages never
leave your network.

### Exposure: VPN only

The web port is the bot's only inbound surface. Publish it to the host's
loopback only and reach it over a VPN such as Tailscale — never publish it to
the internet:

```bash
# on the server, with the container's WEB__PORT published as 127.0.0.1:8081
tailscale serve --bg --https=443 http://127.0.0.1:8081
# then open https://<server>.<tailnet>.ts.net from a device on your tailnet
```

`tailscale serve` also provides the HTTPS certificate. Responses carry a strict
Content-Security-Policy, and API responses are never cached.

> `docker-compose.yml` does not pass the `WEB__*` settings or publish the web
> port yet; that arrives with the client. Until then the channel can be enabled
> when running the bot directly.

### Devices

Each device gets its own token. Only a SHA-256 hash of it is stored, so the
token is shown once, at creation:

```bash
docker compose exec mindbot dotnet MindBot.Bot.dll device add phone
docker compose exec mindbot dotnet MindBot.Bot.dll device list
docker compose exec mindbot dotnet MindBot.Bot.dll device revoke phone
```

Revoking takes effect on the device's next request, and closes any WebSocket it
has open before another message reaches it (an idle socket is re-checked at
least once a minute). All devices share one conversation,
so history, a `/new` in progress and late replies follow you across devices.

### API

Every route needs `Authorization: Bearer <token>` except the WebSocket, which
takes the token in its first frame so it never appears in a URL or access log.

| Route | Purpose |
| --- | --- |
| `GET /api/session` | Check a token; returns the device name. |
| `POST /api/messages` | Send `{"clientMessageId": "...", "text": "..."}`. Returns the stored message and the bot's reply. |
| `GET /api/messages?after=<id>&limit=<n>` | History after a cursor, oldest first, with `hasMore`. |
| `GET /api/commands` | The commands available on this instance, for "/" autocomplete. |
| `/api/ws` | Live updates (WebSocket). |

`clientMessageId` is generated by the device, once per message, and reused on
every retry of that message. It is the web channel's duplicate guard: a retry
returns the stored reply and never files a second note, even when several
copies arrive at once.

The WebSocket protocol:

```
client → {"type":"auth","token":"mbt_...","after":<last id the client has>}
server → {"type":"ready","device":"phone"}
server → {"type":"message","message":{...}}    every line after "after", in id order
```

Every line is read from the stored history, so the stream is in id order with no
gaps, and the highest id a client has seen is always a safe cursor to resume
from. Late replies (`/ytsummary` results) and operator alerts are stored too, so
they reach a device that was offline when they happened.

## Health endpoint

The bot serves `GET /health` on port 8080 inside the container. The port is
deliberately not published — the compose healthcheck probes it from inside.

`/health` answers only on 8080, and the web API only on `WEB__PORT`. The split
is made on the port a connection actually arrived on, not the Host header, so
`/health` cannot be reached through the VPN-exposed web port.

```json
{
  "status": "healthy",
  "degraded": false,
  "lastSuccessfulPollUtc": "2026-07-31T14:29:58+00:00",
  "lastSuccessfulPushUtc": "2026-07-31T14:28:11+00:00",
  "queueDepth": 0,
  "unpushedCommitCount": 0,
  "workingTreeDirty": false,
  "lastClassification": "FastForward",
  "stateStoreReachable": true,
  "pollStalled": false,
  "telegramEnabled": true
}
```

A degraded git state reports `degraded: true` but still returns **200**: a
remote that is down for an hour is a condition this bot is designed to ride out,
not a broken container. Only a stalled poller (no successful poll for 180s) or
an unreachable state database returns **503**. With Telegram off there is no
poller, so `pollStalled` is always `false`.

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
boundary every message passes through covers those too. Web device tokens are
redacted the same way, recognised by their `mbt_` prefix wherever they appear.

## Out of scope

Media handling remains out of scope. No operation moves, renames, or deletes a
note — including on collision, which is why filenames gain a `-2` suffix rather
than overwriting.
