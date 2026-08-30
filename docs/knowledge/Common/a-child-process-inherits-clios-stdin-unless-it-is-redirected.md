---
description: ProcessExecutor redirects the child's standard input on every path and closes it immediately, because an inherited stdin is the MCP server's JSON-RPC pipe - git blocked on it for 80s where a shell took 0.06s
applies-to:
  - clio/Common/ProcessExecutor.cs
  - clio.process.fixture/Program.cs
  - clio.tests/Common/ProcessExecutorIntegrationTests.cs
ticket: ENG-96211
date: 2026-08-30
---

**What is true** — `CreateStartInfo` sets `RedirectStandardInput = true` unconditionally, on the capturing
path and on the detached `FireAndForgetAsync` path alike, and the write end is closed the moment the child
starts when no `StandardInput` was supplied. Redirecting here is not about *sending* anything: it is about
not handing the child the handle this process happens to hold. Standard output and standard error still
follow `redirectOutput`, so a detached child can never block on an output pipe nobody drains — do not
"symmetrise" that.

**Why it is this way** — a child that inherits stdin inherits whatever stdin clio has, and when clio runs
as `mcp-server` that is the **JSON-RPC pipe**: a live client writing into it while the runtime reads it.
`git -C <repo> -c core.hooksPath=NUL remote get-url origin` — a local `.git/config` read that takes 0.06 s
from a shell — blocked on that handle and was still alive at 80 s under clio, sampled eight times as the
same PID. Knowledge activation hit its deadline and served nothing. Why git blocks on *that particular*
pipe was never isolated: a plain open pipe as stdin does not reproduce it (0.05 s in a standalone probe),
so the live writer and the concurrent reader both matter. The rule does not depend on knowing.

**What breaks if you ignore it** — two failures, and neither announces itself. A child can block forever on
a handle nobody will ever write to, and the caller sees only a deadline elapsing somewhere unrelated — the
diagnostic that named the real cause was reachable from exactly one command nobody thinks to run. Worse, a
child holding the server's stdin can *consume JSON-RPC bytes*: protocol theft, not merely a hang. The
detached path is the more dangerous of the two, because a browser or an updater holds the handle for the
rest of the session rather than for one command.

**The two launch paths did not hand the child the same kind of stdin, which is why this is easy to
mis-analyse.** Reasoning from the source says both paths inherited the parent's handle and therefore behave
identically. Measured under `dotnet test` on Windows with the fix reverted, a probe reporting
`Console.IsInputRedirected` from inside the child says otherwise:

| launch path | `redirectOutput` | child saw |
|---|---|---|
| `ExecuteAndCaptureAsync`, no input | `true` | `IsInputRedirected=True` — a non-console handle, already at EOF |
| `ExecuteAndCaptureAsync`, input supplied | `true` | `IsInputRedirected=True` — its own pipe |
| `FireAndForgetAsync` | `false` | `IsInputRedirected=**False**` — a real console |

A child reading a *console* to end blocks until Ctrl+Z that never comes. So pre-fix only the detached test
failed; the capture tests passed on that host and prove nothing there. Do not "simplify" the suite on the
theory that the paths are equivalent — they were not.

Because the discriminating half depends on what the host's stdin happens to be, the redirection invariants
themselves are pinned directly instead: `CreateStartInfo` is `internal` and
`CreateStartInfo_ShouldAlwaysRedirectStandardInput` /
`CreateStartInfo_ShouldRedirectOutputOnlyWhenCapturing` assert them on every OS with no process launched.
The integration tests around them (including the negative control
`ReadStandardInputFixture_ShouldBlock_UntilTheParentClosesStandardInput`, which proves an open never-closed
stdin really does hold a child forever) cover the end-to-end behaviour; the unit tests are what cannot pass
with the fix reverted.

**Known residual, deliberately not fixed:** a detached child still *inherits* stdout, which under
`mcp-server` is the JSON-RPC framing — the write-side twin of this problem. It is unreachable today (no
`Command/McpServer/**` code path reaches a fire-and-forget launcher), and the obvious fix is worse than the
bug: closing a detached browser's or updater's stdout hands it a broken pipe. Redirect output on that path
only together with a drain, never on its own.

*Note for future edits:* both closes go through `TryCloseStandardInput`, which swallows. That is
deliberate — the enclosing filter on the capture path is `OperationCanceledException` only, so a bare
`Close()` there would surface an `IOException` as a *launch* failure for a process that started fine.
