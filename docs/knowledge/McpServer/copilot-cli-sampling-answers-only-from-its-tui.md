---
description: GitHub Copilot CLI advertises the MCP sampling capability in every mode but answers sampling/createMessage only from its interactive TUI; `-p` never responds at all
applies-to:
  - clio/Command/McpServer/Relay/McpServerParentSession.cs
  - clio/Command/McpServer/Relay/IWorkerMcpRelay.cs
ticket: ENG-98526
date: 2026-09-14
---

**What is true** — GitHub Copilot CLI (measured on 1.0.83, model claude-sonnet-5) announces
`"sampling": {}` in BOTH handshakes — its proprietary `server/discover` `_meta`
(`io.modelcontextprotocol/clientCapabilities`, protocol `2026-07-28`) and the standard `initialize`
(protocol `2025-11-25`) — in every launch mode. It answers a `sampling/createMessage` request only
when it is running its interactive TUI. In `-p` (prompt / non-interactive) mode it returns
**nothing at all**: no result, no JSON-RPC error, no `notifications/cancelled`. Measured with a
standalone stdio probe server, clio not involved:

| launch | outcome |
|---|---|
| `copilot -p`, piped stdio | silent past 90 s |
| `copilot -p` inside a PTY (tmux) | silent past 90 s |
| interactive TUI, prompt typed in | answered in 0.6 s |

The third row is the discriminator: a PTY does not help, so the trigger is `-p` itself, not the
absence of a terminal. Claude Code (2.1.266) is the opposite case and is safe either way: it
advertises no `sampling` capability at all, and a request sent to it anyway is refused immediately
with `-32601 Method not found`.

**Why it is this way** — in Copilot's bundle the handler is
`j.on("sampling.requested", ...)`, registered inside a React/Ink component's `useEffect`, and every
exit from it (approved, rejected, cancelled) sends the response from that UI tree. Under `-p` the
tree is not mounted, so no listener exists and nothing — not even a rejection — is ever written to
the wire. Its own debug log shows an internal
`sampling request cancelled before the host responded` that never leaves the process.

**What breaks if you ignore it** — a server→client MCP request to such a client never completes and
never throws, so a `try/catch`-based degrade cannot fire and the tool hangs forever. That is exactly
what happened to `update-page` / `sync-pages`: the CAADT release tests hung at the 2700 s cap on
every configuration that writes a Freedom UI page. If you add any new server→client request
(sampling, elicitation, roots), bound it with a linked `CancellationTokenSource` and a deadline —
silence is a legitimate client state, not an exceptional one.
