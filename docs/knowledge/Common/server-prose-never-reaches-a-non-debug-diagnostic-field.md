---
description: server-authored text (ErrorCode=5, login page, proxy page) may never be embedded in error/cause/log; the fixed local sentence goes there and the correlation ID is the only bridge back to the raw excerpt on the debug channel
applies-to:
  - clio/Common/AuthenticationFailureClassifier.cs
  - clio/Common/ClassifyingDataProvider.cs
  - clio/Common/ISysSettingsManager.cs
  - clio/Common/SessionRejectedException.cs
  - clio/Common/DataProviderFailureException.cs
  - clio/Command/SysSettingsCommand.cs
  - clio/Command/McpServer/SensitiveErrorTextRedactor.cs
  - clio/ExceptionReadableMessageExtension.cs
  - clio/Common/ServerReportedFailureText.cs
ticket: GH-1333
date: 2026-09-03
---

**What is true** — no diagnostic clio surfaces by default may contain text the server authored. A
recognized authentication cause is named by one of the fixed sentences in
`AuthenticationFailureClassifier.FixedAuthenticationDiagnostics`; the server text is used only to
CHOOSE the sentence. The neutralized excerpt travels on `IServerDetailCarrier.ServerDetail`
(`SessionRejectedException`, `DataProviderFailureException`) and reaches exactly one sink:
`ILogger.WriteDebug`, which `ConsoleLogger` drops unless `--debug` was passed. The operation's
correlation ID appears on both the failure envelope and that debug line, and is the only bridge
between them.

The scrub-and-fence applied at the `WriteDebug` call site is **load-bearing, not redundant**.
`ConsoleLogger.WriteDebug` suppresses the console *drain* under MCP server mode, but it still
`CaptureMessage`s into the per-flow buffer that `BaseTool` harvests into
`CommandExecutionResult.Messages` — so "console-suppressed under MCP" does not mean "cannot reach an
envelope". `ExceptionReadableMessageExtension` renders the same excerpt for the CLI and applies the
same treatment, and it also renders a carrier's OWN message rather than an inner one: an
`InvalidOperationException` arm that preferred `InnerException.Message` was printing the raw parser
fault instead of the composed diagnosis.

The single exception is a plain `Success == false` whose `ErrorMessage` is the platform's own
validation prose ("Column 'Name' is required") — no fixed sentence can replace it without destroying
the diagnosis. That one is kept, but passed through
`SensitiveErrorTextRedactor.RedactUntrustedOrNull`, which scrubs URIs/paths/tokens, flattens line
breaks, clamps the length, and wraps the remainder in the `[untrusted-source-text …]` fence.

**Why it is this way** — an `ErrorCode:5` envelope, a login page and a proxy page are all text a
third party chooses. Stripping control characters (which is all `TextUtilities.SanitizeForDisplay`
does) leaves a bearer token, a user's e-mail address, a bidi override that reorders the rendered
line, and a sentence shaped like an instruction. Every embedding site forwarded it to three sinks at
once: the CLI output, the log file, and the MCP envelope — which an AI agent reads as part of its own
context, in a server whose tool surface includes destructive tools.

**What breaks if you ignore it** — reintroducing `{detail}` / `{body}` into a message, a `cause`, or
a non-debug log line reopens all three: a token leaks into a log an operator pastes into a ticket, a
customer's address leaks into an agent transcript, and an agent reads attacker-chosen prose as
guidance. It is silent: nothing fails, the text simply appears where it should not.

## Two renderings, one per sink

The fence is part of the **agent** rendering only. `[untrusted-source-text begin] … [end]` exists so a
model reading an MCP envelope field can tell observed data from an instruction; a terminal is not a
model's context window, so on the console the markers have no audience and read as clio
malfunctioning — `clio set-syssetting` printed `SysSettings with code: UsrX is not updated.
[untrusted-source-text begin] Column 'Name' is required. [untrusted-source-text end]` for an ordinary
platform validation failure.

So a failure composed from server prose carries **both** renderings and each sink picks:

| Rendering | Produced by | Read by |
| --- | --- | --- |
| fenced | `UntrustedText.Fenced` → `SensitiveErrorTextRedactor.RedactUntrustedOrNull` | MCP envelope fields, `WriteDebug` (which MCP mode still captures) |
| unfenced | `UntrustedText.ForConsole` → `SensitiveErrorTextRedactor.RedactForConsoleOrNull` | `ILogger.WriteError` lines that are not MCP-visible, `GetReadableMessageException` at default verbosity |

`ServerReportedFailureText.ConsoleCause` / `ComposeConsoleMessage` and
`IConsoleRenderedFailure.ConsoleMessage` (implemented by `DataProviderFailureException`) are the seams.
`Exception.Message` deliberately stays the fenced form, so every existing consumer is unchanged and only
a console-only sink reads the other one. Dropping the fence does **not** drop the neutralization: the
console rendering is still scrubbed, flattened and length-capped.

`SysSettingsCommand.WriteAndForwardFailureLine` is the one path that keeps the fence while writing to the
console, because the same line is forwarded to `McpLogNotifier` — it *is* MCP-visible. What changed there
is only that `DescribeFailureForLog` no longer prints the same composed diagnostic as both `Error` and
`Cause`, which used to put two fence pairs on one line.

## Layering: the `Clio.Common` seam and the deferred move

`SensitiveErrorTextRedactor` still lives in `namespace Clio.Command.McpServer` while being the
product-wide untrusted-text rule. `clio/Common/UntrustedText.cs` is the `Clio.Common`-owned seam that
every `Common` call site depends on instead, and it is deliberately the only file under `clio/Common`
that names the MCP namespace for this rule (`CreatioUninstaller`'s
`Clio.Command.McpServer.Progress` import is a separate, older edge).

**Deferred:** moving `SensitiveErrorTextRedactor` into `Clio.Common` is a ~90-file mechanical change,
left out of issue #1333 on purpose. **Owner:** whoever next touches redaction broadly; the move now
touches `UntrustedText.cs` rather than the call sites. Until then, do not add a new
`using Clio.Command.McpServer;` to a file under `clio/Common` — route it through `UntrustedText`, or the
inverted edge is silently normalized and `Common` can no longer be reasoned about without the MCP module.
