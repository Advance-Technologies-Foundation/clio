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

Note on transports: `add-knowledge-source` documents credential-free public HTTPS Git and NuGet
locations. Serving guidance from a local working copy is the subject of in-flight work on
`fix/local-knowledge-source`, so the exact wiring may differ by branch — which is precisely why the
commit assertion above is mandatory rather than advisory.

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

The verb is `push-pkg` (`push-package` does not exist). Confirm the installed version equals the one
just built before running anything.

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

Mode detection is mechanical: `bare` when the `ANTHROPIC_API_KEY` environment variable is set or
`apiKeyHelper` is configured in `~/.claude/settings.json`, otherwise `isolated`. Neither is present on
this machine today, so runs default to `isolated` until an API key is provisioned.

`bare` mode (true clean room; needs `ANTHROPIC_API_KEY` or `apiKeyHelper` — OAuth is never read):

```
claude --bare -p "$(cat <prompt>)" --mcp-config ./mcp-clio-only.json --strict-mcp-config --session-id <uuid> --output-format stream-json
```

`isolated` mode (no API key available):

```
claude -p "$(cat <prompt>)" --mcp-config ./mcp-clio-only.json --strict-mcp-config --session-id <uuid> --output-format stream-json
```

Capture stdout to `<scratch>\<uuid>.stream.json`. The session transcript also lands at
`~\.claude\projects\<scratch-slug>\<uuid>.jsonl` — knowing the id in advance removes the need to
guess which file belongs to this run.

`isolated` mode still loads `~/.claude/CLAUDE.md`, user-level skills, plugins, and hooks. Record the
mode in the report; efficiency counts across modes are not comparable.

## Teardown — restore the guidance configuration

Knowledge sources are configured **globally**, in `%LOCALAPPDATA%\creatio\clio\appsettings.json`.
The local wiring therefore outlives the run and silently applies to every later clio session on this
machine, including ordinary work that has nothing to do with testing.

Restore at the end of every run, including a failed one:

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
