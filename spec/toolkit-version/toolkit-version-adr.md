# Toolkit version reporting

Issue: #1500. Decision: extend `info` (aliases `ver`, `get-version`, `i`) with one
DI-resolved local metadata reader. Reuse the existing filesystem and user-home
abstractions and interface auto-registration. No installer calls, network requests,
new persisted state, or new command options are required.

Read Claude's global user registry, Codex's selected cache manifest, Cursor's local
plugin manifest, and Copilot's installed marketplace plugin manifest. Report each
agent separately. Missing installations and unknown versions are explicit; an
individual read failure does not prevent other component output. Sanitize values
at the console boundary. Component-specific options perform no toolkit reads.

Validation covers all four formats, stale Codex caches, malformed metadata,
absence, and command output. The complete Common unit suite is required because
the reader is under Common. Ring does not consume this command; no Ring protocol
changes are involved. MCP has no dedicated `info` tool and needs no new tool for
this human-facing local diagnostic.
