---
description: CommandLineParser never reveals which alias invoked a verb, and clio/YAML/Step.cs builds options with Activator.CreateInstance, so a guard added only in Program.NormalizeCommandLineArgs does not protect the scenario runner
applies-to:
  - clio/Program.cs
  - clio/YAML/Step.cs
  - clio/Command/SysSettingsCommand.cs
date: 2026-08-19
---

**What is true** — two independent constraints shape every alias- or argument-level fix in clio.
`CommandLineParser` exposes the parsed options object but not the token the user typed, so a verb
that behaves differently under one of its aliases (`get-syssetting` vs `set-syssetting`) can only
be distinguished by inspecting `args[0]` before parsing — that is what
`Program.NormalizeGetSysSettingArgs` does, alongside the `create-data-binding --environment` and
bare `--json` normalizations. Separately, `clio/YAML/Step.cs` instantiates the options type with
`Activator.CreateInstance` and fills properties from the YAML step, so a scenario step never passes
through `NormalizeCommandLineArgs` at all.

**Why it is this way** — argv normalization is a CLI-entry-point transform. The scenario/YAML
runner is a second, parallel entry point into the same command objects that starts after argv is
gone.

**What breaks if you ignore it** — a safety guard implemented purely as argv normalization is
absent for every YAML scenario. `SysSettingsCommand.Execute` therefore repeats the check
(`opts.Value is null` -> error, exit 1) rather than trusting the normalizer; the value-less write it
blocks used to POST `{"<code>":""}` and silently clear the setting. Note the cut line is `is null`,
not `IsNullOrEmpty`: an explicit empty string stays a legitimate "set empty" write, and it must
behave identically on both entry points.
