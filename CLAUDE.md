# Clio — agent instructions

Claude Code loads this file automatically at the start of every session. The
authoritative project rules live in `AGENTS.md` and `project-context.md` (shared
with Codex and other agents). They are imported below so they are always in
context — read them before doing any work.

@AGENTS.md
@project-context.md

## Read-first checklist (details in the imported files)

- **Command pattern:** new CLI commands use `Command<TOptions>` + constructor-injected
  services, registered in `BindingsModule.cs` and wired in `Program.cs`.
  **Do NOT use MediatR** — it has been removed from the codebase.
- **CLI options are kebab-case** (analyzer CLIO001). Treat all `CLIO*` diagnostics as errors.
- **Change a command → update its MCP surface AND docs.** When you touch a command,
  command options, or handler, also review `clio/Command/McpServer/**`
  (tool + prompt + resources + `clio.mcp.e2e`) and the docs
  (`help/en/<verb>.txt`, `docs/commands/<verb>.md`, `Commands.md`, `Wiki/WikiAnchors.txt`).
  A nested `clio/Command/McpServer/CLAUDE.md` carries the MCP-specific rules.
- **Experimental commands are feature-toggled.** Hide a not-for-public command by adding
  `[FeatureToggle("key")]` to its **options class** (and the same attribute to its MCP
  tool/resource/prompt class if it has one — the MCP surface is gated separately). It is
  off by default; enable with `clio experimental --name key --enable`. Full rules and the
  MCP `WithTools(IEnumerable<Type>)` caveat are in `project-context.md` → "Feature toggles".
- **Tests:** `[Category("Unit")]` (never `"UnitTests"`), naming
  `MethodName_ShouldExpectedBehavior_WhenCondition`, AAA + a `because` on every
  assertion + `[Description]` on every test; NUnit/FluentAssertions/NSubstitute;
  command tests prefer `BaseCommandTests<TOptions>`. Run the targeted
  `dotnet test --filter "Category=Unit&Module=<...>"` before committing.
- **Non-trivial feature → run the BMAD pipeline first** (PRD → ADR → stories → test
  plan). No code before the ADR exists. See the "BMAD development pipeline" section
  in `AGENTS.md`.
- **Agentic code review is a mandatory gate.** Run a comprehensive parallel review
  (quality, performance/correctness, security) **before opening a PR** and again **before
  marking it ready-to-merge**; review **every new commit** pushed to an already-open PR,
  but scoped to that commit's diff with a triage skip for zero-risk changes so it never
  slows development. Full rules in `AGENTS.md` → "Code review".
- **Workspace diary:** read recent relevant entries in `.codex/workspace-diary.md`
  before non-trivial work, and append an entry after completing it.
