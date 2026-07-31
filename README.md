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
- **Notes**: each message becomes `{yyyy-MM-dd}T{HHmmss}-{slug}.md` in the
  vault's `05 - Fleeting` folder, with YAML frontmatter (`date`,
  `tags: [WIP, MindBot]`) and the message body verbatim.
- **Git**: the bot drives the `git` CLI directly (no LibGit2Sharp). It only ever
  reads from and writes to one dedicated branch (`GIT__BRANCH`, e.g. `bot-inbox`)
  and never merges, force-pushes, or rewrites history on it.
- **Vault**: Obsidian itself is never invoked — this is plain filesystem work
  on Markdown files. No operation ever moves, renames, or deletes a note.

## Project layout

```
src/
  MindBot.Core/           Options, note/filename/frontmatter logic, IGitService
                          abstraction — no filesystem or process I/O.
  MindBot.Infrastructure/ GitService (git CLI subprocess) and the vault file
                          writer — the only project that touches disk or spawns
                          processes.
  MindBot.Bot/            Worker Service host: DI wiring, config validation,
                          the git startup self-check, and the Telegram polling
                          loop.
tests/
  MindBot.Tests/          xUnit tests, including a GitService suite that runs
                          against a real local bare repository.
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
| `VAULT__ROOT` | yes | Absolute path to the local clone of the vault (typically a mounted named volume). |
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
- the SSH private key read-only at `/run/secrets/git_ssh_key`,
- a `known_hosts` file read-only at `/run/secrets/known_hosts`.

On first start against an empty volume the bot clones the repository, checks
out `GIT__BRANCH` if it already exists on the remote (or creates it from the
default branch and pushes it if it doesn't), and only then starts polling
Telegram — so a misconfigured deploy (bad token, unreachable remote,
non-writable branch) fails the container visibly instead of silently dropping
messages.

## Out of scope

Commands, media, persistence, batching, and conflict classification are
deliberately not handled — see the acceptance criteria in the project brief.
