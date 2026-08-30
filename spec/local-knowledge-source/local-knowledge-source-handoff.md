# Local knowledge source — handoff

Everything a fresh session needs to continue this branch. Nothing here assumes prior context.

## What the branch is

`fix/local-knowledge-source`, two commits off `origin/master`:

| Commit | What |
|---|---|
| `5eeee7710` | `ProcessExecutor`: a captured child no longer inherits the parent's stdin |
| `fd737c645` | Knowledge subsystem: activation diagnostic surfaced, read-only-aware cache deletion, per-rule location messages |

Both came out of one investigation: **a Git knowledge source installs and then serves nothing.**

## The bug that started it

Registering a local checkout as a Git knowledge source succeeded — `info-knowledge` reported
`Installed: yes, Valid: yes` — and `get-guidance` answered *"no compatible verified knowledge bundle is
active"* with an empty catalog. No command said why.

The reason was reachable from exactly one place, `list-knowledge-examples`, which reads
`IKnowledgeBundleActivator.LastDiagnostic`:

> Git knowledge source 'local-dbg' could not be refreshed: The operation-wide Git knowledge
> synchronization deadline elapsed.

The command that hung was `git -C <repo> -c core.hooksPath=NUL remote get-url origin` — a local
`.git/config` read, **0.06 s** from a shell, still alive at **80 s** under clio, sampled eight times as the
same PID (so: one hung process, not a retry loop).

**Cause.** `ProcessExecutor.CreateStartInfo` set
`RedirectStandardInput = !string.IsNullOrEmpty(options.StandardInput)`. With no input to send — the
overwhelming majority of calls — stdin was not redirected, so the child inherited the parent's. When clio
runs as an MCP server that handle is the **JSON-RPC pipe**: a live client writing into it and the runtime
reading it concurrently. Git blocked on it.

Excluded by measurement before landing on this, so nobody re-treads them: dumb-vs-smart HTTP, `libraryId`
mismatch, resource-URI mismatch, a missing `sequence`, proxies, HTTP keep-alive, lock contention, and the
cleared child environment. Each was reproduced and cleared individually.

**Not isolated:** *why* git blocks on that particular pipe. A plain open pipe as stdin does not reproduce
it (0.05 s in a standalone probe), so the live writer and concurrent reader matter. The fix does not depend
on knowing, and stands on its own besides: a child holding the server's stdin can consume JSON-RPC bytes —
protocol theft, not merely a hang.

**Measured effect.** `list-knowledge-examples`: 30 s and refused → **1.9 s with five examples**.
`get-guidance` then served the local library, and an agent run through it completed a task that had failed
twice before.

## Two platform facts worth knowing

- **`creatio-curated` cannot be removed**, only disabled. Its library id `com.creatio.clio` is therefore
  reserved permanently, and a local source must use a different one — which means its resource `uri`
  values must be rewritten to match, or the manifest is rejected.
- **A Git source updates fast-forward only.** Rewriting the served branch makes `install-knowledge` fall
  back to the previous revision; the source has to be removed and re-added.

## Reproducing locally

clio clones with `--filter=blob:none --depth=1`, and **both partial and shallow clones require the SMART
HTTP protocol** — a static file server serves the dumb protocol, where a plain `git clone` silently falls
back to a full clone and succeeds while clio's fails. A ~50-line `git upload-pack` wrapper is enough.

1. `git clone --bare <knowledge repo> <tmp>/clio-knowledge.git`
2. serve it over smart HTTP on `127.0.0.1` (loopback HTTP is accepted; HTTPS is not required)
3. on a branch of that repo, set `libraryId` to something other than `com.creatio.clio` and rewrite every
   `docs://knowledge/com.creatio.clio/` resource `uri` to match
4. `clio experimental --name knowledge-allow-unsequenced --enable` — `bundle-source.json` carries no
   top-level `sequence`, and without this flag the envelope check rejects it
5. `clio add-knowledge-source --alias local --library-id <yours> --type git --location
   http://127.0.0.1:<port>/clio-knowledge.git --branch <branch> --priority 200 --participation authoritative`
6. `clio install-knowledge --source local`
7. `clio disable-knowledge-source --alias creatio-curated` so the local one is what gets served

**Restore afterwards:** re-enable `creatio-curated`, remove the local source, and
`clio experimental --name knowledge-allow-unsequenced --disable`. That flag is persistent and global, and
while it is on the content-integrity check does not apply.

## State when this was handed over

- Branch pushed; `feature/ENG-95891-formula-expressions` is forked from it and carries unrelated work.
- `dotnet test clio.tests --filter "Category=Unit&(Module=Common|Module=McpServer)"` — 4945 passed.
- The review in `local-knowledge-source-review-findings.md` has NOT been addressed. Two findings are
  blockers, and one of them is a regression this branch introduced.
