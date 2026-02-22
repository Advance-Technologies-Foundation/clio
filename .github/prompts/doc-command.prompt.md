---
description: Create help file for {command name} command
name: doc-command
model: Claude Sonnet 4.5 (copilot)
tools: ['read', 'edit', 'search', 'web', 'agent', 'todo']
---

Canonical-Source: docs/agent-instructions/document-command.md
Canonical-Version: 1

Document the `clio` command `${input:command_name}` using the canonical instructions in:
`docs/agent-instructions/document-command.md`

Agent-specific notes:
- Resolve aliases to canonical command name before writing files.
- Update `clio/help/en/<canonical>.txt`, `clio/docs/commands/<canonical>.md`, and `clio/Commands.md` together.
