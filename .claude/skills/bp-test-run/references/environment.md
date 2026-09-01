# Run environment

Paths and commands for `/bp-test-run`. Everything here was verified on this machine; re-verify
rather than trusting a stale line.

## Checkouts

| What | Path | Notes |
|---|---|---|
| clio (this repo) | `C:\Projects\clio` | Executor's tool surface is built from here |
| Guidance library | `C:\Projects\clio-knowledge` | remote `Advance-Technologies-Foundation/clio-knowledge` |
| ProcessBuilder package | `C:\Projects\cli-process-builder` | package source in `packages\CrtProcessBuilder` |
| clio settings | `%LOCALAPPDATA%\creatio\clio\appsettings.json` | environment catalog — **not** under `%APPDATA%` |

## Choosing the stand

There is no committed default: 29 environments are registered and the right one depends on what is
being tested (.NET Framework vs .NET Core behavior differs).

Resolution order:

1. `--env <alias>` on the invocation.
2. The personal default in `~/.config/bp-test/config.json`, e.g. `{ "env": "pb-stand" }`. This file
   is local-only and deliberately outside the repository — a stand choice is personal, not team
   policy.
3. Ask the user, then offer to persist the answer into that file.

Never fall back to clio's *active* environment. It is whatever the last unrelated command left
selected, and installing a test package onto it is how an unrelated stand gets damaged.

Aliases starting with `pb-` are the process-builder stands and are the usual candidates; confirm the
URL with `clio list-environments` before use — several aliases point at the same site, and some
point at sites that no longer exist.

## Phase 1 — build clio, wire local guidance

Build clio from the working tree, then point the knowledge configuration at the local checkout and
install from it. Inspect what is configured first:

```
clio list-knowledge-sources --json
clio info-knowledge --json
```

`info-knowledge` reports `knowledge.root-path`, the installed generation, and the resolved transport
revision. In `--json` the field is `resolvedRevision` (the serializer is camelCase); the human output
labels the same value `Revision`. For a Git source it is the exact resolved commit.

**Gate 1 — identity.** After `install-knowledge` / `update-knowledge`, assert:

```
resolvedRevision from `clio info-knowledge --json`  ==  git -C C:\Projects\clio-knowledge rev-parse HEAD
```

Not equal → stop. A failed update keeps the previous generation and reports no error the caller is
forced to notice; continuing means measuring published guidance and reporting it as local.

**Gate 2 — the library actually serves.** Identity is not sufficient. A Git source writes no
`current.json` freshness marker, and `IsGitRepositoryInstalled` is only `Directory.Exists(<repo>/.git)`
— a half-written checkout satisfies it. When activation then fails, the library is deactivated and
`get-guidance` serves **nothing** (see
`docs/knowledge/McpServer/git-knowledge-sources-have-no-freshness-marker-and-no-startup-self-heal-substitute.md`).

So run a positive control before the executor does: call `get-guidance name=routing` against the
same built clio and confirm real content comes back, plus one article the feature under test depends
on. Without this control a deactivated library produces a transcript full of flailing that the
efficiency rubric will faithfully misattribute to guidance defects — the run would then argue for
rewriting articles that were never read.

**How the wiring actually works — measured, not assumed.** `libraryId` is unique across sources: a
second source declaring `com.creatio.clio` is refused with *"library ... is already configured"*, and
the built-in `creatio-curated` already holds it. So you cannot add a parallel source for the library.
The supported path is to **override the `creatio-curated` alias itself** with a Git source — that is
what it is for (clio#1017, "Allow curated knowledge Git source override") — which means
`remove-knowledge-source` then `add-knowledge-source` under the same alias.

Two things that will stop you the first time:

- `remove-knowledge-source` refuses without `--force` in a non-interactive host. That is deliberate;
  supply it rather than looking for another route.
- The configured `library-id` must equal the one the repository's `bundle-source.json` declares
  (`com.creatio.clio`). Inventing a distinct id to avoid the uniqueness rule gets the checkout
  rejected at install with *"manifest identity ... does not match the configured library"* — the
  message is accurate, the mistake is in the invocation.

Pin the exact content with `--commit <40-hex>`. When the local checkout's commit is pushed, pinning by
SHA against the public repository serves byte-identical content and needs no local-path support; when
it is not pushed, push it or accept that the run does not test what is on disk.

Also confirm the local checkout is on the branch under test and has no uncommitted guidance edits: a
Git source resolves a commit, so unstaged article changes are invisible to the executor.

## Phase 2 — build and install the package

The canonical rebundle is one call, from the clio repo root:

```
pwsh ./rebundle-process-builder.ps1 -PackageRepoPath C:\Projects\cli-process-builder -Version X.Y.Z.W
```

`-Version` is required and must increase on every rebundle. clio compares the shipped version with
the version the environment recorded; an unchanged version reaches new installs only, so an existing
stand is never offered the update and the run silently tests the old package.

Then **rebuild clio** before installing. Install commands resolve the bundled archive from the build
output directory, so a rebundle without a rebuild installs the previous archive.

Install and verify:

```
clio push-pkg <archive> -e <alias>
clio list-packages -e <alias>
```

The verb is `push-pkg` (`push-package` does not exist). Before running anything, confirm the stand is
**not older** than the local build — not that it is equal. A stand ahead of the branch is a normal
state (another branch rebundled a higher version and installed it first), the install is then a no-op
by design, and an equality assertion would abort a run that is perfectly valid. What must abort the
run is a stand *behind* the version the cases require, or behind the local build when the change under
test is what you came to exercise.

Two platform behaviors that fail silently, both relevant here:

- A package is matched by `UId`. For a source-only package, "installed" and "compiled" are different
  states, and no database read distinguishes them — compile before concluding the stand is ready.
- The archive **filename** becomes the package code on upload, and a bad upload poisons the shared
  staging folder so later installs fail with a misleading error.

## Phase 3 — launching the executor

MCP config, next to the scratch directory — clio only, no Atlassian, no browser:

```json
{ "mcpServers": { "clio": { "command": "dotnet", "args": ["<path to built clio.dll>", "mcp-server"] } } }
```

Generate a UUID per run, then launch from a scratch directory **outside** `C:\Projects`:

Isolation detection is mechanical: `bare` when the `ANTHROPIC_API_KEY` environment variable is set or
`apiKeyHelper` is configured in `~/.claude/settings.json`, otherwise `isolated`. Neither is present on
this machine today, so runs default to `isolated` until an API key is provisioned.

`--isolation bare` (true clean room; needs `ANTHROPIC_API_KEY` or `apiKeyHelper` — OAuth is never read):

```
claude --bare -p "$(cat <prompt>)" --mcp-config ./mcp-clio-only.json --strict-mcp-config --session-id <uuid> --output-format stream-json
```

`--isolation isolated` (no API key available):

```
claude -p "$(cat <prompt>)" --mcp-config ./mcp-clio-only.json --strict-mcp-config --session-id <uuid> --output-format stream-json
```

Capture stdout to `<scratch>\<uuid>.stream.json`. The session transcript also lands at
`~\.claude\projects\<scratch-slug>\<uuid>.jsonl` — knowing the id in advance removes the need to
guess which file belongs to this run.

`--isolation isolated` still loads `~/.claude/CLAUDE.md`, user-level skills, plugins, and hooks. Record
the isolation in the report; efficiency counts across isolations are not comparable.

## Teardown — restore the guidance configuration

Knowledge sources are configured **globally**, in `%LOCALAPPDATA%\creatio\clio\appsettings.json`.
The local wiring therefore outlives the run and silently applies to every later clio session on this
machine, including ordinary work that has nothing to do with testing.

Restore at the end of every `agent` run, including a failed one. Because the wiring replaced the
built-in alias, restoring means putting it back exactly — these are its shipped values:

```
clio remove-knowledge-source --alias creatio-curated --force
clio add-knowledge-source --alias creatio-curated --library-id com.creatio.clio --type github-release --location https://api.github.com/ --repository-owner Advance-Technologies-Foundation --repository-name clio-knowledge --asset-name clio-knowledge-bundle.zip --priority 100
```

Capture the source's own JSON with `list-knowledge-sources --json` **before** touching anything, so
the restore uses observed values rather than the ones written here. If a run added an extra alias of
its own, remove that too:

```
clio disable-knowledge-source --alias <local alias>
clio enable-knowledge-source --alias creatio-curated
```

Both are non-destructive: configuration and installed generations survive, so the next run re-enables
the local alias without a fresh clone. `creatio-curated` is the built-in source alias.

Print the restored state (`clio list-knowledge-sources`) in the report. A run that leaves the local
library enabled is a defect of the run, not a detail.

## Scratch layout

```
<scratch>\bp-test-<ENG-KEY>-<uuid>\
  mcp-clio-only.json
  prompt.md            copy of the prompt actually executed
  <uuid>.stream.json   captured event stream — input to the efficiency rubric
```

Scratch is disposable and never committed. The prompt and the report live in `spec/<feature>/`.

## Run manifest — the handoff from `agent` to `browser`

Written by the `agent` run next to its report, at
`spec/<feature>/<feature>-manual-test-run-<YYYY-MM-DD>.manifest.json`. It is the only thing that tells
a `browser` run which processes on a shared stand belong to this test; without it that run has to
guess, and it refuses instead.

```json
{
  "runId": "<uuid, the executor session id>",
  "issue": "ENG-XXXXX",
  "stand": { "alias": "krestov-test", "url": "http://..." },
  "packageVersion": "1.4.0.16",
  "prompt": { "path": "spec/<feature>/<feature>-manual-test-prompt.md", "commit": "<sha>" },
  "isolation": "isolated",
  "processes": [
    { "case": "TC-11", "name": "<process name>", "uid": "<guid>", "package": "Custom" }
  ]
}
```

Record a process the moment it is read back in phase 4, not at the end — a run that stalls halfway
still leaves a usable manifest for what it did create, and that partial verification is worth more
than nothing.

The `browser` run checks `stand.alias` against the environment it was given and stops on a mismatch:
opening a designer on the wrong stand produces confident, entirely fictional verdicts.
