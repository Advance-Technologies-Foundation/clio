---
description: a Git knowledge source writes no current.json and IsGitRepositoryInstalled only checks for .git, so the startup re-sync is the ONLY self-heal for a Git checkout and DescribeStaleCache cannot describe one
applies-to:
  - clio/Command/McpServer/Knowledge/CuratedKnowledgeBootstrapService.cs
  - clio/Command/McpServer/Knowledge/KnowledgeSourceInstallationStore.cs
ticket: ENG-93152
date: 2026-08-26
---

**What is true** — two facts about `KnowledgeSourceType.Git` that no signature states:

- `IsGitRepositoryInstalled` is `Directory.Exists(<repo>/.git)` and nothing more. It does not say the
  working tree is readable, that `bundle-source.json` exists, or that HEAD matches the configured
  `Branch`/`Tag`/`Commit`. An `install-knowledge` interrupted after `git init` but before checkout
  satisfies it.
- The `current.json` startup marker that `IKnowledgeSourceInstallationStore.TryReadStartupState`
  reads is written **only** by the artifact publication path, under `generations/`. The Git install
  path never writes it, so `DescribeStaleCache()` has no input for a Git source: it returns its
  `state is null` branch, which `McpServerCommand.ReportCuratedKnowledgeBootstrap` escalates to
  stderr as "unreadable activation marker".

Together these are why `CuratedKnowledgeBootstrapService.Prepare` deliberately excludes Git from the
offline fast path (`source.Type != KnowledgeSourceType.Git`) and lets it fall through to `Install`.
That fall-through IS the repair: the re-sync fixes a half-written checkout and honors a changed ref.

**Why it is this way** — an artifact source has an immutable, content-addressed generation whose
identity can be recorded once and trusted offline. A Git checkout is mutable, its configured
reference lives in settings that can change between runs, and nothing stamps a generation on disk, so
there is no cheap marker a startup path could consult instead of re-synchronizing.

**What breaks if you ignore it** — extending the offline fast path to Git (to make startup work
offline, say) turns both facts into silent failures. A half-written checkout reports
`Success=true, Installed=true, "…is ready from its local cache."` on every start, then
`KnowledgeMultiSourceActivator.ActivateRepositoryUnderLock` fails the read and `HandleGitFailure`
deactivates the library, so `get-guidance` serves nothing until an operator manually reruns
`install-knowledge`; a changed `branch` in config keeps failing `ValidateConfiguredReference` with the
automatic repair gone. And every healthy start prints the false "unreadable activation marker"
warning on the stderr channel MCP hosts use to surface real startup problems. If the alias was
artifact-backed before being overridden to Git, the stale `current.json` is read instead and the
warning reports the old artifact's `libraryVersion`, `sequence` and age as if they described the Git
checkout. Gate any such fast path on something that predicts activation success — `TryRead`
succeeding — and give Git a freshness marker of its own before letting `DescribeStaleCache` see it.
