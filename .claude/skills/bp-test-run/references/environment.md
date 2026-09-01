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

**How the wiring actually works — measured on a real run, not assumed.** Four facts, each of which
costs an hour if you meet it cold. The ordered commands are in
[runbook.md](runbook.md); this is why they are in that order.

**1. It is a settings-file edit, not a CLI operation.** The override of `creatio-curated` by a Git
source is a supported mechanism — `CuratedKnowledgeBootstrapService.IsCuratedGitOverride` recognizes
it deliberately — but no command reaches it. `remove-knowledge-source` refuses ("built-in knowledge
source cannot be removed") and `add-knowledge-source` refuses ("alias or library is already
configured"), because `libraryId` is unique and the built-in alias holds `com.creatio.clio` forever.
So you edit the entry in `%LOCALAPPDATA%\creatio\clio\appsettings.json` by hand. Neither error
message says so.

The override is only recognized when `location` matches the canonical URL **exactly**:
`https://github.com/Advance-Technologies-Foundation/clio-knowledge.git`, with `priority: 100` and
`participation: authoritative`. Any other address — a loopback included — stops being an override, and
then you need a separate alias, a different `libraryId`, and every `docs://knowledge/com.creatio.clio/...`
uri rewritten.

**2. The library omits `sequence` on purpose, and a flag exists for it.** `clio-knowledge` master does
not declare `sequence` in `bundle-source.json`; the release pipeline supplies it. The Git transport
requires it and otherwise fails with *"manifest identity or required envelope is invalid"*. That is not
a regression in either repository: enable `knowledge-allow-unsequenced`, which derives the sequence from
`libraryVersion`. It also relaxes the equal-sequence guard, so you can edit an article and reinstall
without bumping `libraryVersion` — which is exactly what iterating on guidance needs.

**3. Delete the cache BEFORE installing the Git source.** The derived sequence is short —
`1.13.54` becomes `1013054` — while a release-published sequence is long: `1.13.25` was `1013025000`.
Every derived value is therefore orders of magnitude *below* any release-installed one, and the
rollback guard refuses activation. The failure is silent in the worst way: install reports success,
`info-knowledge` reports `Installed: yes, Valid: yes, Library version 1.13.54`, and `get-guidance`
returns `guidance-unavailable` with an empty `availableGuides` — no guidance at all. So run
`delete-knowledge --source creatio-curated --force` first, and treat this as the reason Gate 2 exists.

**4. A local path is not a supported location.** `ValidateRemoteUri` accepts HTTPS, or HTTP only on
loopback: `file://`, an absolute path and a local bare repository are all rejected, and the validation
runs even on a hand-written entry. So pin a **pushed** commit with `--commit <40-hex>` and get
byte-identical content that way. Serving genuinely uncommitted local edits needs a loopback smart-HTTP
server (clio clones `--filter=blob:none --depth=1`, which the dumb protocol cannot serve). There is no
"point clio at a folder" mode; that is a product gap, not something to work around here.

Also confirm the local checkout has no uncommitted guidance edits: a Git source resolves a commit, so
unstaged article changes are invisible to the executor.

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

## Teardown — three steps, all required

Knowledge sources and feature flags are both configured **globally**, in
`%LOCALAPPDATA%\creatio\clio\appsettings.json`. The wiring therefore outlives the run and applies to every
later clio session on this machine, including work that has nothing to do with testing.

Restore at the end of every `agent` run, including a failed one:

```
clio experimental --name knowledge-allow-unsequenced --disable
```

```
copy appsettings.json.bpskills-backup over appsettings.json
```

```
clio install-knowledge --source creatio-curated
```

Why each one:

- **The flag** is persistent and global, and while it is on the content-integrity check that detects a
  swapped guidance corpus does not apply to this source. Leaving it enabled is the most consequential
  thing a run can forget.
- **The settings copy** is the only exact restore available. `remove-knowledge-source` cannot remove the
  built-in alias, so there is no command that puts the release transport back — take the backup during
  setup and copy it back here. Capture `list-knowledge-sources --json` before touching anything so the
  restore is checked against observed values rather than remembered ones.
- **The reinstall** is needed because setup deleted the release generation to get past the sequence
  guard. Restoring configuration alone leaves the machine configured for a library it has not
  installed — guidance then serves nothing, and the next unrelated session pays for it.

Print the restored state (`clio list-knowledge-sources`, `clio info-knowledge`) in the report. A run
that leaves the local library or the flag in place is a defect of the run, not a detail.

**Teardown does not touch the stand.** The processes the run created are the input to mode `browser`.

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
