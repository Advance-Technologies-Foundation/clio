---
description: the "SysSettings with code: X is not updated." WriteError inside SysSettingsCommand.UpdateSysSetting is the only failure signal the TryUpdateSysSetting Maintainer flow in Program.cs has - do not downgrade it to WriteWarning
applies-to:
  - clio/Command/SysSettingsCommand.cs
  - clio/Program.cs
date: 2026-08-19
---

**What is true** — `SysSettingsCommand.UpdateSysSetting` writes
`SysSettings with code: {code} is not updated.` through `_logger.WriteError` when the environment
refuses the value, and the `Execute` override adds no second verdict line — it just returns 1. The
error line is the verdict, not a detail beneath one.

**Why it is this way** — `Program.cs` calls `sysSettingsCommand.TryUpdateSysSetting(...)` for the
`Maintainer` setting and discards the result; `TryUpdateSysSetting` returns `void` and swallows the
exception path too. That flow has no other channel. Moving the line to `WriteWarning` to separate
"detail" from "verdict" reads cleaner on the `set-syssetting` surface and was rejected for exactly
this reason: it silently downgrades the Maintainer write to a warning nobody keys on, and it splits
one sentence across two log channels when `Execute` then prints its own verdict.

**What breaks if you ignore it** — a refused `Maintainer` write during `clio` environment setup
prints a warning at most, the run continues, and the package-unlock step that follows fails later
against a maintainer the environment never accepted. The original refusal is by then invisible in
the log.
