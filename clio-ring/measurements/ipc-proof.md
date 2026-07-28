# clio ring — MCP-over-stdio IPC proof

_Generated 2026-07-11 17:26Z by `--ipc-proof`._

**Verdict: PASS** — connected=True, catalog=True, respawn=True.

## Launch

- Command: `dotnet C:\Projects\clio\clio\bin\Debug\net10.0\clio.dll mcp-server`
- Working directory: `(launcher dir)`

## Handshake (initialize)

- Server: **clio 8.1.0.77**
- Protocol version: `2025-11-25`
- Capabilities: `tools`, `resources`, `prompts`, `logging`
- Instructions advertised: yes (1174 chars)

## Measurements

| Step | Value | Notes |
|------|-------|-------|
| Cold start (spawn -> initialized) | 560.7 ms | First ConnectAsync; StdioClientTransport spawns + handshakes in one call. |
| Handshake/warm protocol RTT (ping min/avg/max, n=5) | 0.1/0.4/1.2 ms | Bare MCP ping round-trip; stdio fuses spawn+initialize so this is the steady-state handshake-shaped RTT. |
| Catalog fetch (get-tool-contract {}) | 278.7 ms | 150 tools (71 destructive, 26 resident); modernIndex=True. |
| Warm trivial call (list-environments) | 6.9 ms | isError=False; 23 environments; structuredContent=False. |
| Representative call (clio-run -> describe-environment, env=ve) | 131.8 ms | isError=False; 212 chars returned (env may be offline — call path is what is proven). |
| Child-death -> respawn recovery | 541.3 ms | killedPid=23220; deathObserved=True; postRespawnCallOk=True; server clio 8.1.0.77. |
| Bounded shutdown (stdin close -> exit) | 772.8 ms | DisposeAsync fires the SDK dispose (closes stdin), waits at most a 750ms grace for the owned child, then force-terminates only that child. Bounded so a Ring exit is never blocked; see the 'ipc shutdown: outcome=graceful|forced' log line. This SDK holds stdin until its own timeout, so the real shutdown reports 'forced' at ~750ms. |

## Catalog

- Total tools discovered via `get-tool-contract {}`: **150**
- Destructive tools: 71

---
READ-ONLY proof. No destructive tools were invoked.
