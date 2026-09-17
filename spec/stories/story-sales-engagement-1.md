# Compact sequence context

Issue: clio#1575
Status: validated; delivery pending

As an agent I can retrieve bounded, environment-derived sequence metadata and configuration in one MCP invocation.

Definition of done:
- [x] CLI and environment-aware MCP adapter share behavior.
- [x] Effective fields and lookup/configuration choices are returned without stored GUIDs.
- [x] Missing schema and unreadable metadata are distinguished; truncation is explicit.
- [x] Optional existing sequence inspection reports prerequisites.
- [x] Unit, external MCP and sequence-capable lab checks pass.
- [x] Command docs, tool discovery and Ring compatibility review complete.
